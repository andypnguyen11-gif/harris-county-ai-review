using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.UnitTests.Validation.Reports;

public class ValidationReportDtoTests
{
    private static ValidationReport ReportWith(BoundingBox? box) => new()
    {
        Id = Guid.NewGuid(),
        CaseId = Guid.NewGuid(),
        WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
        CreatedAt = DateTime.UtcNow,
        Items =
        [
            new ValidationReportItem
            {
                Id = Guid.NewGuid(),
                Order = 0,
                RuleName = "RequiredFieldRule(HCAD account number)",
                Requirement = "HCAD account number",
                ValidationType = ValidationType.Deterministic,
                Status = ValidationStatus.Missing,
                Message = "Field 'hcad account number' is present but has no value.",
                PageNumber = box?.PageNumber,
                BoundingBox = box,
            },
        ],
    };

    [Fact]
    public void Exposes_The_Region_On_The_Item()
    {
        var box = new BoundingBox { PageNumber = 1, X = 0.1, Y = 0.2, Width = 0.3, Height = 0.04 };

        var dto = ValidationReportDto.FromEntity(ReportWith(box));

        Assert.Equal(box, dto.Items.Single().BoundingBox);
    }

    [Fact]
    public void Leaves_The_Region_Null_When_The_Item_Has_None()
    {
        var dto = ValidationReportDto.FromEntity(ReportWith(box: null));

        Assert.Null(dto.Items.Single().BoundingBox);
    }

    private static ValidationReportItem Item(int order, string requirement) => new()
    {
        Id = Guid.NewGuid(),
        Order = order,
        RuleName = $"RequiredFieldRule({requirement})",
        Requirement = requirement,
        ValidationType = ValidationType.Deterministic,
        Status = ValidationStatus.Complete,
        Message = $"'{requirement}' is present.",
    };

    [Fact]
    public void FromEntity_Maps_All_Fields()
    {
        var documentId = Guid.NewGuid();
        var report = new ValidationReport
        {
            Id = Guid.NewGuid(),
            CaseId = Guid.NewGuid(),
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            CreatedAt = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc),
            Items =
            [
                new ValidationReportItem
                {
                    Id = Guid.NewGuid(),
                    Order = 0,
                    RuleName = "SignatureRule(Applicant signature)",
                    Requirement = "Applicant signature",
                    ValidationType = ValidationType.Deterministic,
                    Status = ValidationStatus.Missing,
                    Message = "The signature field appears unsigned.",
                    ExtractedValue = "unsigned",
                    DocumentId = documentId,
                    DocumentType = DocumentType.PermitApplication,
                    PageNumber = 3,
                },
            ],
        };

        var dto = ValidationReportDto.FromEntity(report);

        Assert.Equal(report.Id, dto.Id);
        Assert.Equal(report.CaseId, dto.CaseId);
        Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, dto.WorkflowType);
        Assert.Equal(report.CreatedAt, dto.CreatedAt);

        var item = Assert.Single(dto.Items);
        Assert.Equal(report.Items[0].Id, item.Id);
        Assert.Equal("SignatureRule(Applicant signature)", item.RuleName);
        Assert.Equal("Applicant signature", item.Requirement);
        Assert.Equal(ValidationType.Deterministic, item.ValidationType);
        Assert.Equal(ValidationStatus.Missing, item.Status);
        Assert.Equal("The signature field appears unsigned.", item.Message);
        Assert.Equal("unsigned", item.ExtractedValue);
        Assert.Equal(documentId, item.DocumentId);
        Assert.Equal(DocumentType.PermitApplication, item.DocumentType);
        Assert.Equal(3, item.PageNumber);
    }

    [Fact]
    public void FromEntity_Orders_Items_By_Rule_Order()
    {
        var report = new ValidationReport
        {
            Id = Guid.NewGuid(),
            CaseId = Guid.NewGuid(),
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            CreatedAt = DateTime.UtcNow,
            Items = [Item(2, "Third"), Item(0, "First"), Item(1, "Second")],
        };

        var dto = ValidationReportDto.FromEntity(report);

        Assert.Equal(["First", "Second", "Third"], dto.Items.Select(item => item.Requirement));
    }
}
