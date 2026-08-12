using HarrisCountyAI.Application.Validation.Semantic.Prompts;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.EndToEnd;

/// <summary>
/// The semantic half of validation, end to end: judgment calls the
/// deterministic engine must not attempt reach the model through the real
/// prompt, and their verdicts come back as report items a reviewer reads
/// alongside the deterministic ones. Everything the engine can decide in code
/// — applicability, absent documents, absent content — is decided in code, and
/// a model that cannot be reached degrades to "needs a human" rather than to a
/// guess.
/// </summary>
public class SemanticValidationEndToEndTests : EndToEndTestBase, IClassFixture<SqlServerTestDatabase>
{
    private const string ConsistencyRequirement = "Project description consistency with construction type";
    private const string AccessoryUseRequirement = "Accessory building or other use description";

    /// <summary>Work described in prose that the checked construction-type boxes do not account for.</summary>
    private const string InconsistentDescription =
        "Place approximately 400 cubic yards of engineered fill across the rear third of the "
        + "tract, then construct the single family dwelling on the raised pad.";

    public SemanticValidationEndToEndTests(SqlServerTestDatabase database)
        : base(database)
    {
    }

    private Task<Guid> SubmitApplicationAsync(
        Guid caseId,
        string? projectDescription = null,
        string? accessoryUseDescription = null,
        bool accessoryBuildingChecked = false) =>
        SubmitAsync(
            caseId, "permit-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(
                id,
                projectDescription: projectDescription,
                accessoryUseDescription: accessoryUseDescription,
                accessoryBuildingChecked: accessoryBuildingChecked));

    [Fact]
    public async Task A_Description_The_Checked_Boxes_Do_Not_Account_For_Is_Reported_Invalid()
    {
        var caseId = await CreateCaseAsync("Semantic Consistency");
        await SubmitApplicationAsync(caseId, projectDescription: InconsistentDescription);

        LanguageModel.EnqueueContent(
            """
            {"verdict":"fail","reasoning":"The description places engineered fill, but the Fill construction-type box is not checked."}
            """);

        var report = await RunValidationAsync(caseId);
        var item = Item(report, ConsistencyRequirement);

        Assert.Equal("Invalid", item.GetProperty("status").GetString());
        Assert.Equal("Semantic", item.GetProperty("validationType").GetString());
        Assert.Contains("Fill construction-type box", item.GetProperty("message").GetString());

        // Exactly one judgment call was made, and only for the applicable rule.
        var request = Assert.Single(LanguageModel.Requests);
        Assert.Equal(SemanticValidationPrompt.Version, request.PromptVersion);

        // The applicant's text travels as delimited data, and the instruction
        // that governs it travels on its own channel.
        Assert.Contains(SemanticValidationPrompt.DocumentTextBeginDelimiter, request.UserPrompt);
        Assert.Contains(SemanticValidationPrompt.DocumentTextEndDelimiter, request.UserPrompt);
        Assert.Contains(InconsistentDescription, request.UserPrompt);
        Assert.Equal(SemanticValidationPrompt.SystemPrompt, request.SystemPrompt);
        Assert.DoesNotContain(SemanticValidationPrompt.SystemPrompt, request.UserPrompt);
    }

    [Fact]
    public async Task A_Consistent_Description_Passes_And_Sits_Beside_The_Deterministic_Items()
    {
        var caseId = await CreateCaseAsync("Semantic Pass");
        await SubmitApplicationAsync(
            caseId,
            projectDescription: "Construct a single family dwelling with an attached two-car garage.");

        LanguageModel.EnqueueContent(
            """
            {"verdict":"pass","reasoning":"The described dwelling and garage match the Single Family Dwelling box."}
            """);

        var report = await RunValidationAsync(caseId);

        Assert.Equal("Complete", StatusOf(report, ConsistencyRequirement));
        Assert.Equal("Complete", StatusOf(report, "Owner name"));
        Assert.Equal(16, report.GetProperty("items").GetArrayLength());
        Assert.Single(LanguageModel.Requests);
    }

    [Fact]
    public async Task A_Model_Outage_Leaves_The_Semantic_Item_For_A_Human_Rather_Than_Guessing()
    {
        var caseId = await CreateCaseAsync("Semantic Outage");
        await SubmitApplicationAsync(caseId, projectDescription: InconsistentDescription);

        LanguageModel.EnqueueException(new HttpRequestException("The model deployment is unavailable."));

        var report = await RunValidationAsync(caseId);
        var item = Item(report, ConsistencyRequirement);

        Assert.Equal("UnableToDetermine", item.GetProperty("status").GetString());
        Assert.Contains("Semantic evaluation could not run", item.GetProperty("message").GetString());

        // The rest of the report is unaffected: one unreachable model does not
        // cost the reviewer the deterministic findings.
        Assert.Equal("Complete", StatusOf(report, "Applicant signature"));
        Assert.Equal("Complete", StatusOf(report, "Property address"));
    }

    [Fact]
    public async Task A_Malformed_Verdict_Is_Not_Read_As_A_Pass()
    {
        var caseId = await CreateCaseAsync("Semantic Malformed Verdict");
        await SubmitApplicationAsync(caseId, projectDescription: InconsistentDescription);

        LanguageModel.EnqueueContent("Looks fine to me, no JSON here.");

        var report = await RunValidationAsync(caseId);
        var item = Item(report, ConsistencyRequirement);

        Assert.Equal("UnableToDetermine", item.GetProperty("status").GetString());
        Assert.Contains("JSON", item.GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_Absent_Use_Description_Is_Decided_In_Code_Not_By_The_Model()
    {
        var caseId = await CreateCaseAsync("Accessory Without Description");

        // The Accessory Building box is checked, so the form requires a use
        // description — and none was extracted. Whether it is present at all is
        // a deterministic question, so no model call is warranted.
        await SubmitApplicationAsync(caseId, accessoryBuildingChecked: true);

        var report = await RunValidationAsync(caseId);
        var item = Item(report, AccessoryUseRequirement);

        Assert.Equal("Missing", item.GetProperty("status").GetString());
        Assert.Equal("Semantic", item.GetProperty("validationType").GetString());
        Assert.Contains("no use description was found", item.GetProperty("message").GetString());
        Assert.Empty(LanguageModel.Requests);
    }

    [Fact]
    public async Task A_Placeholder_Use_Description_Is_A_Judgment_Call_The_Model_Makes()
    {
        var caseId = await CreateCaseAsync("Accessory With Placeholder");
        await SubmitApplicationAsync(
            caseId,
            accessoryUseDescription: "building",
            accessoryBuildingChecked: true);

        // The use description also reads as the application's narrative
        // description, so both semantic rules apply, in workflow order.
        LanguageModel
            .EnqueueContent(
                """{"verdict":"pass","reasoning":"An accessory building is described and the box is checked."}""")
            .EnqueueContent(
                """{"verdict":"fail","reasoning":"'building' does not say what the structure will be used for."}""");

        var report = await RunValidationAsync(caseId);

        Assert.Equal("Complete", StatusOf(report, ConsistencyRequirement));

        var item = Item(report, AccessoryUseRequirement);
        Assert.Equal("Invalid", item.GetProperty("status").GetString());
        Assert.Contains("what the structure will be used for", item.GetProperty("message").GetString());
        Assert.Equal(2, LanguageModel.Requests.Count);
    }
}
