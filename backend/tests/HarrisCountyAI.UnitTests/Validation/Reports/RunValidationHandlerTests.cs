using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Application.Validation.RunValidation;
using HarrisCountyAI.Application.Validation.Workflows;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.Application;
using HarrisCountyAI.UnitTests.Documents.Extraction;
using HarrisCountyAI.UnitTests.Validation.Workflows;

namespace HarrisCountyAI.UnitTests.Validation.Reports;

public class RunValidationHandlerTests
{
    private readonly FakeCaseRepository _caseRepository = new();
    private readonly FakeNormalizedDocumentRepository _normalizedDocuments = new();
    private readonly FakeValidationReportRepository _reports = new();

    private RunValidationHandler CreateHandler(params IWorkflowDefinition[] workflows) => new(
        _caseRepository,
        _normalizedDocuments,
        _reports,
        new DocumentValidationService(),
        workflows.Length > 0 ? workflows : [new FloodplainDevelopmentPermitWorkflow()]);

    private async Task<Case> AddCaseAsync()
    {
        var @case = Case.Create("HC-2026-0001", "Creek Bend Development", WorkflowType.FloodplainDevelopmentPermit);
        await _caseRepository.AddAsync(@case);
        return @case;
    }

    private async Task<NormalizedDocument[]> SeedCompletePackageAsync(Guid caseId)
    {
        var package = FloodplainSamplePackages.CompletePackage(caseId);
        foreach (var document in package)
        {
            await _normalizedDocuments.AddAsync(document);
        }

        return package;
    }

    [Fact]
    public async Task Returns_Null_And_Persists_Nothing_For_Unknown_Case()
    {
        var dto = await CreateHandler().HandleAsync(Guid.NewGuid());

        Assert.Null(dto);
        Assert.Empty(_reports.Reports);
        Assert.Equal(0, _reports.SaveChangesCallCount);
    }

    [Fact]
    public async Task Persists_Report_With_One_Item_Per_Workflow_Rule()
    {
        var @case = await AddCaseAsync();
        await SeedCompletePackageAsync(@case.Id);
        var expectedRuleCount = new FloodplainDevelopmentPermitWorkflow().BuildRules().Count;

        var dto = await CreateHandler().HandleAsync(@case.Id);

        Assert.NotNull(dto);
        Assert.Equal(@case.Id, dto.CaseId);
        Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, dto.WorkflowType);
        Assert.Equal(expectedRuleCount, dto.Items.Count);
        Assert.All(dto.Items, item => Assert.Equal(ValidationStatus.Complete, item.Status));
        Assert.All(dto.Items, item => Assert.Equal(ValidationType.Deterministic, item.ValidationType));

        var persisted = Assert.Single(_reports.Reports);
        Assert.Equal(dto.Id, persisted.Id);
        Assert.Equal(expectedRuleCount, persisted.Items.Count);
        Assert.Equal(1, _reports.SaveChangesCallCount);
    }

    [Fact]
    public async Task Report_Items_Reference_The_Uploaded_Source_Document()
    {
        var @case = await AddCaseAsync();
        var package = await SeedCompletePackageAsync(@case.Id);
        var application = package.Single(d => d.DocumentType == DocumentType.PermitApplication);

        var dto = await CreateHandler().HandleAsync(@case.Id);

        Assert.NotNull(dto);
        var ownerName = dto.Items.Single(item => item.Requirement == "Owner name");
        Assert.Equal("RequiredFieldRule(Owner name)", ownerName.RuleName);
        Assert.Equal("Jane P. Smith", ownerName.ExtractedValue);
        Assert.Equal(application.DocumentId, ownerName.DocumentId);
        Assert.Equal(DocumentType.PermitApplication, ownerName.DocumentType);
        Assert.Equal(1, ownerName.PageNumber);
    }

    [Fact]
    public async Task Items_Follow_The_Workflow_Rule_Order()
    {
        var @case = await AddCaseAsync();
        await SeedCompletePackageAsync(@case.Id);
        var expectedOrder = new FloodplainDevelopmentPermitWorkflow().BuildRules().Select(rule => rule.Name).ToList();

        var dto = await CreateHandler().HandleAsync(@case.Id);

        Assert.NotNull(dto);
        Assert.Equal(expectedOrder, dto.Items.Select(item => item.RuleName).ToList());
    }

    [Fact]
    public async Task Reports_Missing_And_Undeterminable_Requirements_For_Case_Without_Documents()
    {
        var @case = await AddCaseAsync();

        var dto = await CreateHandler().HandleAsync(@case.Id);

        Assert.NotNull(dto);

        var sitePlan = dto.Items.Single(item => item.Requirement == "Site plan");
        Assert.Equal(ValidationStatus.Missing, sitePlan.Status);
        Assert.Null(sitePlan.DocumentId);

        var elevationCertificate = dto.Items.Single(item => item.Requirement == "FEMA Elevation Certificate");
        Assert.Equal(ValidationStatus.NeedsHumanReview, elevationCertificate.Status);

        var ownerName = dto.Items.Single(item => item.Requirement == "Owner name");
        Assert.Equal(ValidationStatus.UnableToDetermine, ownerName.Status);
    }

    [Fact]
    public async Task Throws_When_No_Workflow_Definition_Matches_The_Case()
    {
        var @case = await AddCaseAsync();
        var handler = new RunValidationHandler(
            _caseRepository,
            _normalizedDocuments,
            _reports,
            new DocumentValidationService(),
            workflows: []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(@case.Id));
    }
}
