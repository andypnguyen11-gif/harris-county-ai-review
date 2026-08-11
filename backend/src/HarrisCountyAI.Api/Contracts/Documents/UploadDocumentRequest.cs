namespace HarrisCountyAI.Api.Contracts.Documents;

/// <summary>Multipart form fields for a document upload.</summary>
public sealed class UploadDocumentRequest
{
    public IFormFile? File { get; init; }

    public string? DocumentType { get; init; }
}
