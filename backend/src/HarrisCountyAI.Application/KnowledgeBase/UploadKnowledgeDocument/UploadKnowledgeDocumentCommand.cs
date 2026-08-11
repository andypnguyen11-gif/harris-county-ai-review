namespace HarrisCountyAI.Application.KnowledgeBase.UploadKnowledgeDocument;

/// <summary>
/// Request to add a reference document to the knowledge base. The file content
/// is expected to have already passed <see cref="Documents.DocumentFileValidator"/>.
/// </summary>
public sealed record UploadKnowledgeDocumentCommand(
    string Title,
    string FileName,
    string ContentType,
    Stream Content,
    string Department,
    string DocumentType,
    string PermitType,
    string? Version = null,
    DateOnly? EffectiveDate = null,
    string? SourceUrl = null);
