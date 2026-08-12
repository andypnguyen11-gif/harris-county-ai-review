using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Application.Validation.Comparison;
using HarrisCountyAI.Application.Validation.Semantic;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Validation.Comparison;

/// <summary>
/// The comparison engine, with the deterministic-first ordering as the
/// subject: facts are settled in code, the model is reached only for the
/// judgment that is left over, and a requirement code has already failed never
/// reaches the model at all.
/// </summary>
public class RequirementComparisonServiceTests
{
    private static readonly Guid CaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-777777777777");

    private readonly FakeRetrievalService _retrieval = new();
    private readonly FakeSemanticValidationService _semantic = new();

    private RequirementComparisonService Service(params IRequirementCatalog[] catalogs) =>
        new(catalogs.Length == 0 ? [new TestCatalog()] : catalogs, _retrieval, _semantic);

    private static RequirementComparisonRequest Request(
        IReadOnlyList<NormalizedDocument> documents,
        bool includeEvidence = false,
        bool allowSemantic = true) => new()
        {
            CaseId = CaseId,
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Documents = documents,
            IncludeRequirementEvidence = includeEvidence,
            AllowSemanticEvaluation = allowSemantic,
        };

    private static NormalizedDocument Application(
        string? address = "1234 Bayou Dr",
        bool? signed = true,
        string? description = "Place fill and build a detached garage.") =>
        BuildApplication(address, signed, description);

    private static NormalizedDocument BuildApplication(string? address, bool? signed, string? description)
    {
        var builder = new NormalizedDocumentBuilder(DocumentType.PermitApplication, CaseId)
            .WithTextField("ADDRESS", address)
            .WithSignature("Signature", signed);
        if (description is not null)
        {
            builder = builder.WithTextField("Project Description", description);
        }

        return builder.Build();
    }

    private static NormalizedDocument SitePlan() =>
        new NormalizedDocumentBuilder(DocumentType.SitePlan, CaseId).Build();

    // --- Deterministic outcomes decide, without a model ---------------------

    [Fact]
    public async Task A_Missing_Required_Document_Is_Decided_In_Code()
    {
        var results = await Service().CompareAsync(Request([Application()]));

        var sitePlan = Single(results, "site-plan");
        Assert.Equal(ValidationStatus.Missing, sitePlan.Status);
        Assert.Equal(ValidationType.Deterministic, sitePlan.EvaluatedBy);
        Assert.DoesNotContain(_semantic.Requests, request => request.Requirement == "Site plan");
    }

    [Fact]
    public async Task A_Missing_Required_Field_Is_Decided_In_Code()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication, CaseId)
            .WithSignature("Signature", true)
            .Build();

        var results = await Service().CompareAsync(Request([document, SitePlan()]));

        var address = Single(results, "property-address");
        Assert.Equal(ValidationStatus.Missing, address.Status);
        Assert.Equal(ValidationType.Deterministic, address.EvaluatedBy);
        Assert.Equal(0, _semantic.CallCount);
    }

    [Fact]
    public async Task A_Blank_Required_Field_Is_Decided_In_Code()
    {
        var results = await Service().CompareAsync(Request([Application(address: "   "), SitePlan()]));

        var address = Single(results, "property-address");
        Assert.Equal(ValidationStatus.Missing, address.Status);
        Assert.Contains("blank", address.Message);
        Assert.Equal(ValidationType.Deterministic, address.EvaluatedBy);
    }

    [Fact]
    public async Task An_Unsigned_Signature_Is_Decided_In_Code()
    {
        var results = await Service().CompareAsync(Request([Application(signed: false), SitePlan()]));

        var signature = Single(results, "applicant-signature");
        Assert.Equal(ValidationStatus.Missing, signature.Status);
        Assert.Contains("not signed", signature.Message);
        Assert.Equal(ValidationType.Deterministic, signature.EvaluatedBy);
    }

    [Fact]
    public async Task A_Satisfied_Requirement_Without_A_Judgment_Never_Reaches_The_Model()
    {
        var results = await Service().CompareAsync(Request([Application(), SitePlan()]));

        var sitePlan = Single(results, "site-plan");
        Assert.Equal(ValidationStatus.Complete, sitePlan.Status);
        Assert.Equal(ValidationType.Deterministic, sitePlan.EvaluatedBy);

        // Only the one requirement that carries a criterion may be evaluated.
        Assert.Equal(1, _semantic.CallCount);
        Assert.Equal("Project description consistency", Assert.Single(_semantic.Requests).Requirement);
    }

    [Fact]
    public async Task A_Conditional_Requirement_Absent_Is_Referred_To_A_Reviewer_Not_Called_Missing()
    {
        var results = await Service().CompareAsync(Request([Application(), SitePlan()]));

        var conditional = Single(results, "conditional-document");
        Assert.Equal(ValidationStatus.NeedsHumanReview, conditional.Status);
        Assert.Equal(ValidationType.Deterministic, conditional.EvaluatedBy);
    }

    // --- The model is reached only for the leftover judgment ----------------

    [Fact]
    public async Task A_Judgment_Requirement_Reaches_The_Model_Only_After_Its_Facts_Check_Out()
    {
        _semantic.Verdict = SemanticVerdict.Pass;

        var results = await Service().CompareAsync(Request([Application(), SitePlan()]));

        var consistency = Single(results, "description-consistency");
        Assert.Equal(ValidationStatus.Complete, consistency.Status);
        Assert.Equal(ValidationType.Semantic, consistency.EvaluatedBy);
        // What code had already established, kept alongside the final verdict.
        Assert.Equal(ValidationStatus.Complete, consistency.DeterministicStatus);
        Assert.Equal("semantic-validation/v2", consistency.PromptVersion);
        Assert.Equal("fake-deployment", consistency.ModelDeployment);
    }

    [Fact]
    public async Task A_Judgment_Requirement_Whose_Field_Is_Absent_Never_Reaches_The_Model()
    {
        // The description is what the judgment is about; with no description
        // there is nothing to judge, and its absence is a plain fact.
        var results = await Service().CompareAsync(
            Request([Application(description: null), SitePlan()]));

        var consistency = Single(results, "description-consistency");
        Assert.Equal(ValidationStatus.Missing, consistency.Status);
        Assert.Equal(ValidationType.Deterministic, consistency.EvaluatedBy);
        Assert.Equal(0, _semantic.CallCount);
    }

    [Fact]
    public async Task A_Judgment_Requirement_Whose_Document_Is_Absent_Never_Reaches_The_Model()
    {
        var results = await Service().CompareAsync(Request([SitePlan()]));

        var consistency = Single(results, "description-consistency");
        Assert.Equal(ValidationStatus.Missing, consistency.Status);
        Assert.Equal(ValidationType.Deterministic, consistency.EvaluatedBy);
        Assert.Equal(0, _semantic.CallCount);
    }

    [Fact]
    public async Task The_Model_Only_Sees_The_Content_The_Judgment_Is_About()
    {
        await Service().CompareAsync(
            Request([Application(description: "Place fill and build a detached garage."), SitePlan()]));

        var request = Assert.Single(_semantic.Requests);
        Assert.Equal("Place fill and build a detached garage.", request.DocumentText);
        // The criterion, not the applicant's text, is the trusted instruction.
        Assert.Contains("consistent", request.RequirementDescription);
    }

    [Theory]
    [InlineData(SemanticVerdict.Pass, ValidationStatus.Complete)]
    [InlineData(SemanticVerdict.Fail, ValidationStatus.Invalid)]
    [InlineData(SemanticVerdict.NeedsHumanReview, ValidationStatus.NeedsHumanReview)]
    [InlineData(SemanticVerdict.UnableToDetermine, ValidationStatus.UnableToDetermine)]
    public async Task Every_Semantic_Verdict_Maps_To_A_Status(
        SemanticVerdict verdict,
        ValidationStatus expected)
    {
        _semantic.Verdict = verdict;

        var results = await Service().CompareAsync(Request([Application(), SitePlan()]));

        Assert.Equal(expected, Single(results, "description-consistency").Status);
    }

    [Fact]
    public async Task Disabling_Semantic_Evaluation_Leaves_The_Judgment_To_A_Reviewer()
    {
        var results = await Service().CompareAsync(
            Request([Application(), SitePlan()], allowSemantic: false));

        var consistency = Single(results, "description-consistency");
        Assert.Equal(ValidationStatus.NeedsHumanReview, consistency.Status);
        Assert.Equal(ValidationType.Deterministic, consistency.EvaluatedBy);
        Assert.Equal(0, _semantic.CallCount);

        // The deterministic verdicts are unaffected by the switch.
        Assert.Equal(ValidationStatus.Complete, Single(results, "site-plan").Status);
    }

    [Fact]
    public async Task No_Requirement_Reaches_The_Model_When_Nothing_Was_Submitted()
    {
        var results = await Service().CompareAsync(Request([]));

        Assert.All(results, result => Assert.Equal(ValidationType.Deterministic, result.EvaluatedBy));
        Assert.Equal(0, _semantic.CallCount);
    }

    // --- Evidence ----------------------------------------------------------

    [Fact]
    public async Task Submission_Evidence_Points_At_The_Field_A_Reviewer_Should_Check()
    {
        var application = Application();

        var results = await Service().CompareAsync(Request([application, SitePlan()]));

        var evidence = Assert.Single(Single(results, "property-address").SubmissionEvidence);
        Assert.Equal(application.Id, evidence.DocumentId);
        Assert.Equal(DocumentType.PermitApplication, evidence.DocumentType);
        Assert.Equal("ADDRESS", evidence.FieldName);
        Assert.Equal("1234 Bayou Dr", evidence.ExtractedValue);
        Assert.Equal(1, evidence.Page);
    }

    [Fact]
    public async Task Requirement_Evidence_Is_Retrieved_From_The_County_Corpus_Only()
    {
        _retrieval.ChunksToReturn = [FakeRetrievalService.Chunk()];

        var results = await Service().CompareAsync(
            Request([Application(), SitePlan()], includeEvidence: true));

        Assert.NotEmpty(_retrieval.Requests);
        Assert.All(
            _retrieval.Requests,
            request =>
            {
                Assert.Equal(SourceType.County, request.Scope);
                Assert.Null(request.CaseId);
            });

        var evidence = Assert.Single(Single(results, "site-plan").RequirementEvidence);
        Assert.Equal("Floodplain Regulations", evidence.Title);
        Assert.Equal("https://www.hcfcd.org/regulations", evidence.SourceUrl);
    }

    [Fact]
    public async Task Requirement_Evidence_Is_Skipped_Entirely_When_Not_Requested()
    {
        await Service().CompareAsync(Request([Application(), SitePlan()], includeEvidence: false));

        Assert.Empty(_retrieval.Requests);
    }

    [Fact]
    public async Task A_Retrieval_Failure_Does_Not_Change_Any_Verdict()
    {
        _retrieval.ExceptionToThrow = new InvalidOperationException("search unavailable");

        var results = await Service().CompareAsync(
            Request([Application(), SitePlan()], includeEvidence: true));

        // Evidence is for the reviewer; verdicts come from the catalog and the
        // submitted documents, so losing it must not change an outcome.
        Assert.Equal(ValidationStatus.Complete, Single(results, "site-plan").Status);
        Assert.Empty(Single(results, "site-plan").RequirementEvidence);
    }

    // --- Catalog wiring ----------------------------------------------------

    [Fact]
    public async Task Results_Come_Back_In_Catalog_Order()
    {
        var results = await Service().CompareAsync(Request([Application(), SitePlan()]));

        Assert.Equal(
            ["site-plan", "property-address", "applicant-signature", "conditional-document", "description-consistency"],
            results.Select(result => result.Requirement.Id));
    }

    [Fact]
    public async Task An_Unknown_Workflow_Is_Rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service().CompareAsync(new RequirementComparisonRequest
            {
                CaseId = CaseId,
                WorkflowType = (WorkflowType)999,
                Documents = [],
            }));
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service().CompareAsync(Request([Application(), SitePlan()]), cancellation.Token));
    }

    private static RequirementComparisonResult Single(
        IReadOnlyList<RequirementComparisonResult> results,
        string requirementId) =>
        Assert.Single(results, result => result.Requirement.Id == requirementId);

    /// <summary>
    /// A small catalog covering every shape the engine has to handle: a
    /// document-only requirement, field requirements, a conditional one, and
    /// one that carries a judgment criterion.
    /// </summary>
    private sealed class TestCatalog : IRequirementCatalog
    {
        public WorkflowType WorkflowType => WorkflowType.FloodplainDevelopmentPermit;

        public IReadOnlyList<Requirement> GetRequirements() =>
        [
            new Requirement
            {
                Id = "site-plan",
                WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
                Label = "Site plan",
                Description = "A site plan is required.",
                RequiredDocumentType = DocumentType.SitePlan,
            },
            new Requirement
            {
                Id = "property-address",
                WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
                Label = "Property address",
                Description = "The application must state the property address.",
                RequiredDocumentType = DocumentType.PermitApplication,
                RequiredFieldNames = ["ADDRESS", "Property Address"],
            },
            new Requirement
            {
                Id = "applicant-signature",
                WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
                Label = "Applicant signature",
                Description = "The application must be signed.",
                RequiredDocumentType = DocumentType.PermitApplication,
                RequiredFieldNames = ["Signature"],
            },
            new Requirement
            {
                Id = "conditional-document",
                WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
                Label = "FEMA Elevation Certificate",
                Description = "An elevation certificate is required for Class II submissions.",
                RequiredDocumentType = DocumentType.ElevationCertificate,
                IsConditional = true,
            },
            new Requirement
            {
                Id = "description-consistency",
                WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
                Label = "Project description consistency",
                Description = "The description must match the checked construction types.",
                RequiredDocumentType = DocumentType.PermitApplication,
                RequiredFieldNames = ["Project Description"],
                SemanticCriterion = "The narrative description must be consistent with the checked "
                    + "construction type boxes.",
            },
        ];
    }
}
