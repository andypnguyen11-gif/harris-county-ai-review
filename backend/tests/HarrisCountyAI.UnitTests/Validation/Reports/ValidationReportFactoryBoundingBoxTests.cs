using HarrisCountyAI.Application.Validation.RunValidation;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.UnitTests.Validation.Reports;

public class ValidationReportFactoryBoundingBoxTests
{
    private static readonly BoundingBox Region = new()
    {
        PageNumber = 2,
        X = 0.1,
        Y = 0.2,
        Width = 0.3,
        Height = 0.04,
    };

    private static ValidationResult ResultWith(BoundingBox? box, Guid? sourceDocumentId) => new()
    {
        Requirement = "HCAD account number",
        Status = ValidationStatus.Missing,
        Message = "Field 'hcad account number' is present but has no value.",
        ValidationType = ValidationType.Deterministic,
        RuleName = "RequiredFieldRule(HCAD account number)",
        SourceDocumentId = sourceDocumentId,
        Page = box?.PageNumber,
        BoundingBox = box,
    };

    [Fact]
    public void Copies_The_Region_Onto_The_Report_Item()
    {
        var report = ValidationReportFactory.Create(
            Guid.NewGuid(),
            WorkflowType.FloodplainDevelopmentPermit,
            [ResultWith(Region, sourceDocumentId: null)],
            []);

        Assert.Equal(Region, report.Items.Single().BoundingBox);
    }

    [Fact]
    public void Leaves_The_Region_Null_When_The_Result_Has_None()
    {
        var report = ValidationReportFactory.Create(
            Guid.NewGuid(),
            WorkflowType.FloodplainDevelopmentPermit,
            [ResultWith(box: null, sourceDocumentId: null)],
            []);

        Assert.Null(report.Items.Single().BoundingBox);
    }
}
