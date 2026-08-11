using HarrisCountyAI.Application.Validation.RunValidation;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.UnitTests.Validation.Reports;

public class ValidationReportFactoryTests
{
    private static ValidationResult Result(
        string requirement = "Owner name",
        ValidationStatus status = ValidationStatus.Complete,
        string? extractedValue = null,
        Guid? sourceDocumentId = null,
        int? page = null) => new()
    {
        Requirement = requirement,
        Status = status,
        Message = $"'{requirement}' was checked.",
        ExtractedValue = extractedValue,
        SourceDocumentId = sourceDocumentId,
        Page = page,
        ValidationType = ValidationType.Deterministic,
        RuleName = $"RequiredFieldRule({requirement})",
    };

    [Fact]
    public void Sets_Report_Metadata()
    {
        var caseId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var report = ValidationReportFactory.Create(caseId, WorkflowType.FloodplainDevelopmentPermit, [Result()], []);

        Assert.NotEqual(Guid.Empty, report.Id);
        Assert.Equal(caseId, report.CaseId);
        Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, report.WorkflowType);
        Assert.InRange(report.CreatedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public void Creates_One_Item_Per_Result_In_Rule_Order()
    {
        var results = new[]
        {
            Result("Development permit application"),
            Result("Site plan", ValidationStatus.Missing),
            Result("Owner name", ValidationStatus.UnableToDetermine),
        };

        var report = ValidationReportFactory.Create(Guid.NewGuid(), WorkflowType.FloodplainDevelopmentPermit, results, []);

        Assert.Equal(3, report.Items.Count);
        Assert.Equal([0, 1, 2], report.Items.Select(item => item.Order));
        Assert.Equal(
            ["Development permit application", "Site plan", "Owner name"],
            report.Items.Select(item => item.Requirement));

        var sitePlan = report.Items.Single(item => item.Requirement == "Site plan");
        Assert.Equal(ValidationStatus.Missing, sitePlan.Status);
        Assert.Equal("RequiredFieldRule(Site plan)", sitePlan.RuleName);
        Assert.Equal("'Site plan' was checked.", sitePlan.Message);
        Assert.Equal(ValidationType.Deterministic, sitePlan.ValidationType);
        Assert.NotEqual(Guid.Empty, sitePlan.Id);
    }

    [Fact]
    public void Resolves_Source_Reference_To_The_Uploaded_Document()
    {
        var normalized = new NormalizedDocumentBuilder(DocumentType.PermitApplication).Build();
        var result = Result(extractedValue: "Jane P. Smith", sourceDocumentId: normalized.Id, page: 2);

        var report = ValidationReportFactory.Create(
            normalized.CaseId, WorkflowType.FloodplainDevelopmentPermit, [result], [normalized]);

        var item = Assert.Single(report.Items);
        Assert.Equal("Jane P. Smith", item.ExtractedValue);
        Assert.Equal(normalized.DocumentId, item.DocumentId);
        Assert.Equal(DocumentType.PermitApplication, item.DocumentType);
        Assert.Equal(2, item.PageNumber);
    }

    [Fact]
    public void Leaves_Document_Reference_Empty_When_The_Result_Has_No_Source()
    {
        var report = ValidationReportFactory.Create(
            Guid.NewGuid(),
            WorkflowType.FloodplainDevelopmentPermit,
            [Result("Site plan", ValidationStatus.Missing)],
            [new NormalizedDocumentBuilder(DocumentType.PermitApplication).Build()]);

        var item = Assert.Single(report.Items);
        Assert.Null(item.DocumentId);
        Assert.Null(item.DocumentType);
        Assert.Null(item.PageNumber);
    }

    [Fact]
    public void Leaves_Document_Reference_Empty_When_The_Source_Is_Not_Among_The_Documents()
    {
        var report = ValidationReportFactory.Create(
            Guid.NewGuid(),
            WorkflowType.FloodplainDevelopmentPermit,
            [Result(sourceDocumentId: Guid.NewGuid(), page: 1)],
            []);

        var item = Assert.Single(report.Items);
        Assert.Null(item.DocumentId);
        Assert.Null(item.DocumentType);
        Assert.Equal(1, item.PageNumber);
    }
}
