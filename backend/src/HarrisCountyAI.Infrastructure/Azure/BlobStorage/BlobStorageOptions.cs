namespace HarrisCountyAI.Infrastructure.Azure.BlobStorage;

/// <summary>
/// Configuration for Azure Blob Storage, bound from the
/// <see cref="SectionName"/> configuration section.
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// Azure Storage connection string. Use <c>UseDevelopmentStorage=true</c>
    /// for the local Azurite emulator.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container holding case-specific uploaded documents.</summary>
    public string CaseDocumentsContainerName { get; set; } = "case-documents";

    /// <summary>Container holding the Harris County reference corpus.</summary>
    public string KnowledgeBaseContainerName { get; set; } = "knowledge-base";

    /// <summary>Maximum allowed upload size in bytes. Defaults to 50 MB.</summary>
    public long MaxFileSizeBytes { get; set; } = 50L * 1024 * 1024;
}
