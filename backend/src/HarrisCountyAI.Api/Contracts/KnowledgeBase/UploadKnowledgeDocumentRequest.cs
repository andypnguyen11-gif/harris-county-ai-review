namespace HarrisCountyAI.Api.Contracts.KnowledgeBase;

/// <summary>
/// Multipart form payload for adding a reference document to the knowledge
/// base. <see cref="EffectiveDate"/> is an ISO 8601 date string (yyyy-MM-dd),
/// parsed and validated in the controller.
/// </summary>
public class UploadKnowledgeDocumentRequest
{
    public IFormFile? File { get; init; }

    public string? Title { get; init; }

    public string? Department { get; init; }

    public string? DocumentType { get; init; }

    public string? PermitType { get; init; }

    public string? Version { get; init; }

    public string? EffectiveDate { get; init; }

    public string? SourceUrl { get; init; }
}
