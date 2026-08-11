using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.KnowledgeBase;

/// <summary>Wire representation of a knowledge document. Enums are serialized as strings.</summary>
public sealed record KnowledgeDocumentDto(
    Guid Id,
    string Title,
    string FileName,
    string BlobPath,
    string Department,
    string DocumentType,
    string PermitType,
    string? Version,
    DateOnly? EffectiveDate,
    string? SourceUrl,
    KnowledgeDocumentIngestionStatus IngestionStatus,
    DateTime? IngestionDate,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static KnowledgeDocumentDto FromEntity(KnowledgeDocument document) => new(
        document.Id,
        document.Title,
        document.FileName,
        document.BlobPath,
        document.Department,
        document.DocumentType,
        document.PermitType,
        document.Version,
        document.EffectiveDate,
        document.SourceUrl,
        document.IngestionStatus,
        document.IngestionDate,
        document.CreatedAt,
        document.UpdatedAt);
}
