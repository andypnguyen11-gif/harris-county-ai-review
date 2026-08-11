using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Application.Validation.Workflows;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.UnitTests.Validation.Workflows;

public class FloodplainDevelopmentPermitWorkflowTests
{
    private static readonly string[] ExpectedRequirements =
    [
        "Development permit application",
        "Site plan",
        "FEMA Elevation Certificate",
        "Property address",
        "HCAD account number",
        "Owner name",
        "Applicant printed name",
        "Fill acknowledgement initials",
        "Applicant signature",
        "Application date",
        "Construction type selection",
        "Driveway status selection",
        "Sewer and water system selection",
        "Building code selection",
    ];

    private readonly FloodplainDevelopmentPermitWorkflow _workflow = new();

    private static async Task<IReadOnlyList<ValidationResult>> RunAsync(NormalizedDocument[] package)
    {
        var context = new ValidationContext(
            Guid.NewGuid(),
            WorkflowType.FloodplainDevelopmentPermit,
            package);
        var service = new DocumentValidationService();
        return await service.ValidateAsync(context, new FloodplainDevelopmentPermitWorkflow().BuildRules());
    }

    [Fact]
    public void WorkflowType_Is_FloodplainDevelopmentPermit()
    {
        Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, _workflow.WorkflowType);
    }

    [Fact]
    public void BuildRules_Returns_Expected_Requirements_In_Order()
    {
        var rules = _workflow.BuildRules();

        Assert.Equal(ExpectedRequirements.Length, rules.Count);
        Assert.Equal(
            ["RequiredDocumentRule(Development permit application)", "RequiredDocumentRule(Site plan)"],
            rules.Take(2).Select(r => r.Name));
    }

    [Fact]
    public void BuildRules_Returns_Fresh_Instances_Per_Call()
    {
        Assert.NotSame(_workflow.BuildRules()[0], _workflow.BuildRules()[0]);
    }

    [Fact]
    public async Task Complete_Package_Satisfies_Every_Requirement()
    {
        var results = await RunAsync(FloodplainSamplePackages.CompletePackage());

        Assert.Equal(
            ExpectedRequirements.Select(requirement => (requirement, ValidationStatus.Complete)),
            results.Select(result => (result.Requirement, result.Status)));
        Assert.All(results, result => Assert.Equal(ValidationType.Deterministic, result.ValidationType));
    }

    [Fact]
    public async Task Complete_Package_Propagates_Extracted_Values_And_Sources()
    {
        var package = FloodplainSamplePackages.CompletePackage();
        var applicationId = package[0].Id;

        var results = await RunAsync(package);
        var byRequirement = results.ToDictionary(result => result.Requirement);

        Assert.Equal("Jane P. Smith", byRequirement["Owner name"].ExtractedValue);
        Assert.Equal("1234567890123", byRequirement["HCAD account number"].ExtractedValue);
        Assert.Equal("06/01/2026", byRequirement["Application date"].ExtractedValue);
        Assert.Equal("Single Family Dwelling (includes garage)", byRequirement["Construction type selection"].ExtractedValue);
        Assert.Equal(applicationId, byRequirement["Applicant signature"].SourceDocumentId);
        Assert.Equal(1, byRequirement["Applicant signature"].Page);
    }

    [Fact]
    public async Task Incomplete_Package_Reports_Unsigned_Signature_And_Absent_Elevation_Certificate()
    {
        var results = await RunAsync(FloodplainSamplePackages.IncompletePackage());

        var expected = ExpectedRequirements.Select(requirement => (requirement, requirement switch
        {
            "FEMA Elevation Certificate" => ValidationStatus.NeedsHumanReview,
            "Applicant signature" => ValidationStatus.Missing,
            _ => ValidationStatus.Complete,
        }));
        Assert.Equal(expected, results.Select(result => (result.Requirement, result.Status)));

        var signature = results.Single(result => result.Requirement == "Applicant signature");
        Assert.Contains("not signed", signature.Message);

        var elevationCertificate = results.Single(result => result.Requirement == "FEMA Elevation Certificate");
        Assert.Contains("Class II", elevationCertificate.Message);
        Assert.Contains("reviewer must confirm", elevationCertificate.Message);
    }

    [Fact]
    public async Task Invalid_Package_Reports_Bad_Date_And_Unchecked_Required_Boxes()
    {
        var results = await RunAsync(FloodplainSamplePackages.InvalidPackage());

        var expected = ExpectedRequirements.Select(requirement => (requirement, requirement switch
        {
            "Application date" => ValidationStatus.Invalid,
            "Construction type selection" => ValidationStatus.Missing,
            "Building code selection" => ValidationStatus.Missing,
            _ => ValidationStatus.Complete,
        }));
        Assert.Equal(expected, results.Select(result => (result.Requirement, result.Status)));

        var date = results.Single(result => result.Requirement == "Application date");
        Assert.Equal("13/45/2026", date.ExtractedValue);
        Assert.Contains("could not be read as a date", date.Message);

        var constructionType = results.Single(result => result.Requirement == "Construction type selection");
        Assert.Contains("None of the checkboxes", constructionType.Message);
    }

    [Fact]
    public async Task Empty_Package_Reports_Missing_Documents_And_UnableToDetermine_Fields()
    {
        var results = await RunAsync([]);

        var expected = ExpectedRequirements.Select(requirement => (requirement, requirement switch
        {
            "Development permit application" => ValidationStatus.Missing,
            "Site plan" => ValidationStatus.Missing,
            "FEMA Elevation Certificate" => ValidationStatus.NeedsHumanReview,
            _ => ValidationStatus.UnableToDetermine,
        }));
        Assert.Equal(expected, results.Select(result => (result.Requirement, result.Status)));
    }
}
