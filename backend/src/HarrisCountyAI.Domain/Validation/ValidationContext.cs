using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Validation;

/// <summary>Everything a validation rule may inspect: the case's normalized documents plus lookup helpers.</summary>
public sealed class ValidationContext
{
    public ValidationContext(Guid caseId, WorkflowType workflowType, IReadOnlyList<NormalizedDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        CaseId = caseId;
        WorkflowType = workflowType;
        Documents = documents;
    }

    public Guid CaseId { get; }

    public WorkflowType WorkflowType { get; }

    public IReadOnlyList<NormalizedDocument> Documents { get; }

    public bool HasDocumentType(DocumentType documentType) =>
        Documents.Any(document => document.DocumentType == documentType);

    public IReadOnlyList<NormalizedDocument> GetDocuments(DocumentType documentType) =>
        Documents.Where(document => document.DocumentType == documentType).ToList();

    /// <summary>
    /// Finds the first field whose name matches any of the given name variants, comparing
    /// case-insensitively and ignoring whitespace and punctuation (so "OWNER NAME", "Owner Name:"
    /// and "owner_name" all match). Optionally restricted to documents of one type.
    /// </summary>
    public FieldMatch? FindField(IReadOnlyCollection<string> nameVariants, DocumentType? documentType = null)
    {
        ArgumentNullException.ThrowIfNull(nameVariants);

        var normalizedVariants = nameVariants
            .Select(NormalizeFieldName)
            .Where(variant => variant.Length > 0)
            .ToHashSet();

        if (normalizedVariants.Count == 0)
        {
            return null;
        }

        foreach (var document in Documents)
        {
            if (documentType is not null && document.DocumentType != documentType)
            {
                continue;
            }

            foreach (var field in document.Fields)
            {
                if (normalizedVariants.Contains(NormalizeFieldName(field.Name)))
                {
                    return new FieldMatch(document, field);
                }
            }
        }

        return null;
    }

    /// <summary>Lower-cases a field name and strips everything but letters and digits, so OCR renderings of the same label compare equal.</summary>
    public static string NormalizeFieldName(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
