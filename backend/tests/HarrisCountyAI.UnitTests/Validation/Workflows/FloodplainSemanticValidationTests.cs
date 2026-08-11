using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Application.Validation.Semantic;
using HarrisCountyAI.Application.Validation.Workflows;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using HarrisCountyAI.UnitTests.Common.AI;

namespace HarrisCountyAI.UnitTests.Validation.Workflows;

/// <summary>Tests for the semantic rules section of the floodplain development permit workflow.</summary>
public class FloodplainSemanticValidationTests
{
    private const string ConsistencyRequirement = "Project description consistency with construction type";
    private const string AccessoryRequirement = "Accessory building or other use description";

    private readonly FakeLanguageModelService _model = new();

    private FloodplainDevelopmentPermitWorkflow CreateWorkflow() =>
        new(new SemanticValidationService(_model));

    private static NormalizedDocument Application(
        string? projectDescription = null,
        string? accessoryUseDescription = null,
        bool accessoryBuildingChecked = false,
        bool singleFamilyChecked = true)
    {
        var builder = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithCheckbox("Single Family Dwelling (includes garage)", isChecked: singleFamilyChecked)
            .WithCheckbox("Accessory Building", isChecked: accessoryBuildingChecked);
        if (projectDescription is not null)
        {
            builder.WithTextField("Project Description", projectDescription);
        }

        if (accessoryUseDescription is not null)
        {
            builder.WithTextField("Describe use of Accessory Building or Other", accessoryUseDescription);
        }

        return builder.Build();
    }

    private static Task<IReadOnlyList<ValidationResult>> RunAsync(
        IReadOnlyList<IValidationRule> rules,
        params NormalizedDocument[] documents) =>
        new DocumentValidationService().ValidateAsync(
            NormalizedDocumentBuilder.ContextFor(documents), rules);

    [Fact]
    public void Without_Semantic_Service_The_Workflow_Stays_Deterministic_Only()
    {
        var workflow = new FloodplainDevelopmentPermitWorkflow();

        Assert.Empty(workflow.BuildSemanticRules());
        Assert.Equal(workflow.BuildDeterministicRules().Count, workflow.BuildRules().Count);
    }

    [Fact]
    public void With_Semantic_Service_BuildRules_Appends_The_Semantic_Section_After_The_Deterministic_Rules()
    {
        var workflow = CreateWorkflow();

        var deterministic = workflow.BuildDeterministicRules();
        var semantic = workflow.BuildSemanticRules();
        var all = workflow.BuildRules();

        Assert.Equal(14, deterministic.Count);
        Assert.Equal(
            [ConsistencyRequirement, AccessoryRequirement],
            semantic.Select(rule => ((SemanticEvaluationRule)rule).Requirement));
        Assert.Equal(deterministic.Count + semantic.Count, all.Count);
        Assert.All(all.Take(deterministic.Count), rule => Assert.IsNotType<SemanticEvaluationRule>(rule));
        Assert.All(all.Skip(deterministic.Count), rule => Assert.IsType<SemanticEvaluationRule>(rule));
    }

    [Fact]
    public async Task Consistent_Description_Passes_With_Semantic_Result_Type()
    {
        _model.EnqueueContent("""{"verdict": "pass", "reasoning": "The described dwelling matches the checked box."}""");
        var application = Application(projectDescription: "New single family home with attached garage.");

        var results = await RunAsync(CreateWorkflow().BuildSemanticRules(), application);

        var consistency = results.Single(result => result.Requirement == ConsistencyRequirement);
        Assert.Equal(ValidationStatus.Complete, consistency.Status);
        Assert.Equal(ValidationType.Semantic, consistency.ValidationType);
        Assert.Equal("The described dwelling matches the checked box.", consistency.Message);
    }

    [Fact]
    public async Task Inconsistent_Description_Fails_With_The_Model_Reasoning()
    {
        _model.EnqueueContent("""{"verdict": "fail", "reasoning": "The description mentions fill but the Fill box is unchecked."}""");
        var application = Application(projectDescription: "Placing 200 cubic yards of fill on the lot.");

        var results = await RunAsync(CreateWorkflow().BuildSemanticRules(), application);

        var consistency = results.Single(result => result.Requirement == ConsistencyRequirement);
        Assert.Equal(ValidationStatus.Invalid, consistency.Status);
        Assert.Contains("Fill box is unchecked", consistency.Message);
    }

    [Fact]
    public async Task Consistency_Prompt_Contains_Checked_Boxes_And_Description()
    {
        _model.EnqueueContent("""{"verdict": "pass", "reasoning": "OK."}""");
        var application = Application(projectDescription: "New single family home.");

        await RunAsync(CreateWorkflow().BuildSemanticRules(), application);

        var prompt = _model.Requests.Single().UserPrompt;
        Assert.Contains("Single Family Dwelling (includes garage)", prompt);
        Assert.Contains("New single family home.", prompt);
    }

    [Fact]
    public async Task Consistency_Check_Skips_Without_Model_Call_When_No_Description_Exists()
    {
        var results = await RunAsync(CreateWorkflow().BuildSemanticRules(), Application());

        var consistency = results.Single(result => result.Requirement == ConsistencyRequirement);
        Assert.Equal(ValidationStatus.Complete, consistency.Status);
        Assert.Contains("Not applicable", consistency.Message);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task Accessory_Check_Skips_Without_Model_Call_When_Box_Is_Unchecked()
    {
        var results = await RunAsync(
            CreateWorkflow().BuildSemanticRules(),
            Application(accessoryBuildingChecked: false));

        var accessory = results.Single(result => result.Requirement == AccessoryRequirement);
        Assert.Equal(ValidationStatus.Complete, accessory.Status);
        Assert.Contains("Not applicable", accessory.Message);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task Accessory_Box_Checked_Without_Description_Reports_Missing_Without_Model_Call()
    {
        var results = await RunAsync(
            CreateWorkflow().BuildSemanticRules(),
            Application(accessoryBuildingChecked: true));

        var accessory = results.Single(result => result.Requirement == AccessoryRequirement);
        Assert.Equal(ValidationStatus.Missing, accessory.Status);
        Assert.Contains("no use description was found", accessory.Message);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task Adequate_Accessory_Use_Description_Passes()
    {
        // The use description also serves as the narrative description, so the consistency rule
        // runs first and consumes the first scripted response; the accessory rule gets the second.
        _model.EnqueueContent("""{"verdict": "pass", "reasoning": "Consistent."}""");
        _model.EnqueueContent("""{"verdict": "pass", "reasoning": "The use is specific."}""");

        var results = await RunAsync(
            CreateWorkflow().BuildSemanticRules(),
            Application(
                accessoryBuildingChecked: true,
                accessoryUseDescription: "Detached workshop for personal woodworking."));

        var accessory = results.Single(result => result.Requirement == AccessoryRequirement);
        Assert.Equal(ValidationStatus.Complete, accessory.Status);
        Assert.Equal(ValidationType.Semantic, accessory.ValidationType);
    }

    [Fact]
    public async Task Vague_Accessory_Use_Description_Needs_Human_Review()
    {
        // First response feeds the consistency rule, second feeds the accessory rule.
        _model.EnqueueContent("""{"verdict": "needs_human_review", "reasoning": "Too vague to compare."}""");
        _model.EnqueueContent("""{"verdict": "needs_human_review", "reasoning": "The entry 'building' does not explain the use."}""");

        var results = await RunAsync(
            CreateWorkflow().BuildSemanticRules(),
            Application(accessoryBuildingChecked: true, accessoryUseDescription: "building"));

        var accessory = results.Single(result => result.Requirement == AccessoryRequirement);
        Assert.Equal(ValidationStatus.NeedsHumanReview, accessory.Status);
    }

    [Fact]
    public async Task Model_Failure_Surfaces_As_UnableToDetermine_And_Deterministic_Rules_Are_Unaffected()
    {
        _model.EnqueueException(new TimeoutException("model timed out"));
        var application = Application(projectDescription: "New single family home.");
        var sitePlan = new NormalizedDocumentBuilder(DocumentType.SitePlan).Build();

        var results = await RunAsync(CreateWorkflow().BuildRules(), application, sitePlan);

        var consistency = results.Single(result => result.Requirement == ConsistencyRequirement);
        Assert.Equal(ValidationStatus.UnableToDetermine, consistency.Status);
        Assert.Equal(ValidationType.Semantic, consistency.ValidationType);

        var sitePlanResult = results.Single(result => result.Requirement == "Site plan");
        Assert.Equal(ValidationStatus.Complete, sitePlanResult.Status);
        Assert.Equal(ValidationType.Deterministic, sitePlanResult.ValidationType);
    }
}
