using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Documents.Normalization;

/// <summary>
/// Converts raw extraction output into the vendor-neutral
/// <see cref="NormalizedDocument"/> shape the rest of the system consumes.
/// </summary>
public interface IDocumentNormalizationService
{
    NormalizedDocument Normalize(Guid caseId, DocumentType documentType, ExtractedDocument extracted);
}
