using Azure.Storage.Blobs;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.BlobStorage;

/// <summary>
/// Service registration for Azure Blob Storage document storage.
/// </summary>
public static class BlobStorageServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IDocumentStorageService"/> backed by Azure Blob
    /// Storage, along with <see cref="BlobStorageOptions"/> bound from the
    /// <c>BlobStorage</c> configuration section and a
    /// <see cref="DocumentFileValidator"/> configured with the section's
    /// maximum file size.
    /// </summary>
    public static IServiceCollection AddBlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BlobStorageOptions>()
            .Bind(configuration.GetSection(BlobStorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"{BlobStorageOptions.SectionName}:{nameof(BlobStorageOptions.ConnectionString)} is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.CaseDocumentsContainerName),
                $"{BlobStorageOptions.SectionName}:{nameof(BlobStorageOptions.CaseDocumentsContainerName)} is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.KnowledgeBaseContainerName),
                $"{BlobStorageOptions.SectionName}:{nameof(BlobStorageOptions.KnowledgeBaseContainerName)} is required.")
            .Validate(
                options => options.MaxFileSizeBytes > 0,
                $"{BlobStorageOptions.SectionName}:{nameof(BlobStorageOptions.MaxFileSizeBytes)} must be greater than zero.")
            .ValidateOnStart();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
            return CreateBlobServiceClient(options.ConnectionString, provider.GetResilienceOptions());
        });

        services.AddSingleton<IDocumentStorageService, AzureBlobDocumentStorageService>();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
            return new DocumentFileValidator(options.MaxFileSizeBytes);
        });

        return services;
    }

    /// <summary>
    /// Creates a <see cref="BlobServiceClient"/> pinned to service API version
    /// 2025-11-05 — the newest version the local Azurite emulator (3.36.x)
    /// accepts, and a GA version supported by Azure Storage — with the shared
    /// retry and timeout budget applied.
    /// </summary>
    public static BlobServiceClient CreateBlobServiceClient(
        string connectionString,
        AzureResilienceOptions? resilience = null)
        => new(
            connectionString,
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2025_11_05)
                .WithResilience(resilience ?? new AzureResilienceOptions()));
}
