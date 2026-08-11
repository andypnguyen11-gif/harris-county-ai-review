using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Validation;

/// <summary>Wire representation of a validation report. Enums are serialized as strings.</summary>
public sealed record ValidationReportDto(
    Guid Id,
    Guid CaseId,
    WorkflowType WorkflowType,
    DateTime CreatedAt,
    IReadOnlyList<ValidationReportItemDto> Items)
{
    public static ValidationReportDto FromEntity(ValidationReport report) => new(
        report.Id,
        report.CaseId,
        report.WorkflowType,
        report.CreatedAt,
        report.Items
            .OrderBy(item => item.Order)
            .Select(ValidationReportItemDto.FromEntity)
            .ToList());
}

/// <summary>Wire representation of a single rule outcome within a validation report.</summary>
public sealed record ValidationReportItemDto(
    Guid Id,
    string RuleName,
    string Requirement,
    ValidationType ValidationType,
    ValidationStatus Status,
    string Message,
    string? ExtractedValue,
    Guid? DocumentId,
    DocumentType? DocumentType,
    int? PageNumber)
{
    public static ValidationReportItemDto FromEntity(ValidationReportItem item) => new(
        item.Id,
        item.RuleName,
        item.Requirement,
        item.ValidationType,
        item.Status,
        item.Message,
        item.ExtractedValue,
        item.DocumentId,
        item.DocumentType,
        item.PageNumber);
}
