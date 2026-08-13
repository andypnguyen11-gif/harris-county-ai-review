using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.Application.Validation.RunValidation;

/// <summary>
/// Builds a persistable <see cref="ValidationReport"/> from the results of a validation run.
/// Rule results reference the normalized document their evidence came from; the factory resolves
/// that reference back to the uploaded document's id and type so the report is self-contained.
/// </summary>
public static class ValidationReportFactory
{
    public static ValidationReport Create(
        Guid caseId,
        WorkflowType workflowType,
        IReadOnlyList<ValidationResult> results,
        IReadOnlyList<NormalizedDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(documents);

        var documentsById = documents.ToDictionary(document => document.Id);

        return new ValidationReport
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            WorkflowType = workflowType,
            CreatedAt = DateTime.UtcNow,
            Items = results.Select((result, index) => CreateItem(result, index, documentsById)).ToList(),
        };
    }

    private static ValidationReportItem CreateItem(
        ValidationResult result,
        int order,
        IReadOnlyDictionary<Guid, NormalizedDocument> documentsById)
    {
        NormalizedDocument? source = null;
        if (result.SourceDocumentId is { } sourceDocumentId)
        {
            documentsById.TryGetValue(sourceDocumentId, out source);
        }

        return new ValidationReportItem
        {
            Id = Guid.NewGuid(),
            Order = order,
            RuleName = result.RuleName,
            Requirement = result.Requirement,
            ValidationType = result.ValidationType,
            Status = result.Status,
            Message = result.Message,
            ExtractedValue = result.ExtractedValue,
            DocumentId = source?.DocumentId,
            DocumentType = source?.DocumentType,
            PageNumber = result.Page,
            BoundingBox = result.BoundingBox,
        };
    }
}
