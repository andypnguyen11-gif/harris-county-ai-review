using HarrisCountyAI.Application.Validation.Comparison;
using HarrisCountyAI.Application.Validation.Semantic;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.QuestionAnswering;

namespace HarrisCountyAI.UnitTests.Validation.Comparison;

/// <summary>
/// The floodplain requirement catalog: the county requirements the engine
/// compares against, and the discipline that keeps judgment out of the ones
/// code can settle.
/// </summary>
public class FloodplainDevelopmentPermitRequirementsTests
{
    private readonly FloodplainDevelopmentPermitRequirements _catalog = new();

    [Fact]
    public void The_Catalog_Describes_The_Floodplain_Development_Permit()
    {
        Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, _catalog.WorkflowType);
        Assert.All(
            _catalog.GetRequirements(),
            requirement => Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, requirement.WorkflowType));
    }

    [Fact]
    public void Requirement_Ids_Are_Unique_And_Non_Empty()
    {
        var requirements = _catalog.GetRequirements();

        Assert.NotEmpty(requirements);
        Assert.All(requirements, requirement => Assert.False(string.IsNullOrWhiteSpace(requirement.Id)));
        Assert.Equal(
            requirements.Count,
            requirements.Select(requirement => requirement.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_Requirement_Names_Its_Source_And_Explains_Itself()
    {
        Assert.All(_catalog.GetRequirements(), requirement =>
        {
            Assert.False(string.IsNullOrWhiteSpace(requirement.Label));
            Assert.False(string.IsNullOrWhiteSpace(requirement.Description));
            Assert.False(string.IsNullOrWhiteSpace(requirement.SourceReference));
        });
    }

    [Fact]
    public void The_Regulation_Required_Documents_Are_Present()
    {
        var documentTypes = _catalog.GetRequirements()
            .Where(requirement => requirement.RequiredFieldNames.Count == 0)
            .Select(requirement => requirement.RequiredDocumentType)
            .ToList();

        Assert.Contains(DocumentType.PermitApplication, documentTypes);
        Assert.Contains(DocumentType.SitePlan, documentTypes);
        Assert.Contains(DocumentType.ElevationCertificate, documentTypes);
    }

    [Fact]
    public void The_Elevation_Certificate_Is_Conditional()
    {
        // Its permit class cannot be derived from extracted data, so its
        // absence must not be reported as a plain omission.
        var requirement = Assert.Single(
            _catalog.GetRequirements(), r => r.Id == "elevation-certificate");

        Assert.True(requirement.IsConditional);
    }

    [Fact]
    public void Presence_Requirements_Carry_No_Semantic_Criterion()
    {
        // A missing address, a blank field, an unsigned signature: all facts.
        // Attaching a criterion to one of these would send it to a model that
        // has nothing to add.
        string[] purelyDeterministic =
        [
            "permit-application",
            "site-plan",
            "elevation-certificate",
            "property-address",
            "hcad-account-number",
            "owner-name",
            "applicant-signature",
            "application-date",
        ];

        foreach (var id in purelyDeterministic)
        {
            var requirement = Assert.Single(_catalog.GetRequirements(), r => r.Id == id);
            Assert.Null(requirement.SemanticCriterion);
        }
    }

    [Fact]
    public void Only_The_Two_Judgment_Requirements_Carry_A_Criterion()
    {
        var withCriterion = _catalog.GetRequirements()
            .Where(requirement => requirement.SemanticCriterion is not null)
            .Select(requirement => requirement.Id)
            .ToList();

        Assert.Equal(["project-description-consistency", "accessory-use-description"], withCriterion);
    }

    [Fact]
    public async Task Comparing_An_Empty_Submission_Calls_No_Model_At_All()
    {
        var semantic = new FakeSemanticValidationService();
        var service = new RequirementComparisonService([_catalog], new FakeRetrievalService(), semantic);

        var results = await service.CompareAsync(new RequirementComparisonRequest
        {
            CaseId = Guid.NewGuid(),
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Documents = [],
            IncludeRequirementEvidence = false,
        });

        Assert.Equal(_catalog.GetRequirements().Count, results.Count);
        Assert.Equal(0, semantic.CallCount);
        Assert.All(results, result => Assert.Equal(ValidationType.Deterministic, result.EvaluatedBy));
    }

    [Fact]
    public async Task A_Complete_Application_Sends_Only_Its_Judgment_Requirements_To_The_Model()
    {
        var semantic = new FakeSemanticValidationService { Verdict = SemanticVerdict.Pass };
        var service = new RequirementComparisonService([_catalog], new FakeRetrievalService(), semantic);

        var application = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("ADDRESS", "1234 Bayou Dr")
            .WithTextField("HCAD Account Number", "0123456789")
            .WithTextField("OWNER NAME", "Jane Doe")
            .WithSignature("Signature", true)
            .WithDateField("Date", "2026-01-15")
            .WithTextField("Project Description", "Place fill and build a detached garage.")
            .WithTextField("Describe use of Accessory Building or Other", "Detached workshop for woodworking.")
            .Build();

        var results = await service.CompareAsync(new RequirementComparisonRequest
        {
            CaseId = Guid.NewGuid(),
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Documents =
            [
                application,
                new NormalizedDocumentBuilder(DocumentType.SitePlan).Build(),
                new NormalizedDocumentBuilder(DocumentType.ElevationCertificate).Build(),
            ],
            IncludeRequirementEvidence = false,
        });

        Assert.Equal(2, semantic.CallCount);
        Assert.All(results, result => Assert.Equal(ValidationStatus.Complete, result.Status));
        Assert.Equal(
            2,
            results.Count(result => result.EvaluatedBy == ValidationType.Semantic));
    }
}
