using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Documents;

/// <summary>Wire representation of an uploaded document. Enums are serialized as strings.</summary>
public sealed record DocumentDto(
    Guid Id,
    Guid CaseId,
    string FileName,
    DocumentType DocumentType,
    DocumentProcessingStatus ProcessingStatus,
    DateTime CreatedAt)
{
    public static DocumentDto FromEntity(Document document) => new(
        document.Id,
        document.CaseId,
        document.FileName,
        document.DocumentType,
        document.ProcessingStatus,
        document.CreatedAt);
}
