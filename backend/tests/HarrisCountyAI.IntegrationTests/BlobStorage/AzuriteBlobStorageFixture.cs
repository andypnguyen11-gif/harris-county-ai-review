using Azure.Storage.Blobs;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.IntegrationTests.BlobStorage;

/// <summary>
/// Shared fixture for blob storage integration tests. Talks to the local
/// Azurite emulator with a unique pair of containers per test run and deletes
/// them on cleanup.
/// </summary>
public sealed class AzuriteBlobStorageFixture : IAsyncLifetime
{
    public const string ConnectionString = "UseDevelopmentStorage=true";

    public AzuriteBlobStorageFixture()
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        Options = new BlobStorageOptions
        {
            ConnectionString = ConnectionString,
            CaseDocumentsContainerName = $"test-case-documents-{runId}",
            KnowledgeBaseContainerName = $"test-knowledge-base-{runId}",
        };
        Client = BlobStorageServiceExtensions.CreateBlobServiceClient(ConnectionString);
        Service = new AzureBlobDocumentStorageService(
            Client, Microsoft.Extensions.Options.Options.Create(Options));
    }

    public BlobStorageOptions Options { get; }

    public BlobServiceClient Client { get; }

    public AzureBlobDocumentStorageService Service { get; }

    public async Task InitializeAsync()
    {
        try
        {
            await Client.GetPropertiesAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Azurite is not reachable at localhost:10000. Blob storage integration tests " +
                "require the local emulator; start it with 'docker compose up -d' from the repository root.",
                ex);
        }
    }

    public async Task DisposeAsync()
    {
        await Client.GetBlobContainerClient(Options.CaseDocumentsContainerName).DeleteIfExistsAsync();
        await Client.GetBlobContainerClient(Options.KnowledgeBaseContainerName).DeleteIfExistsAsync();
    }
}
