namespace HarrisCountyAI.Domain.Enums;

/// <summary>Progress of an uploaded document through the extraction pipeline.</summary>
public enum DocumentProcessingStatus
{
    Pending,
    Uploaded,
    Extracting,
    Extracted,
    Normalized,
    Failed,
}
