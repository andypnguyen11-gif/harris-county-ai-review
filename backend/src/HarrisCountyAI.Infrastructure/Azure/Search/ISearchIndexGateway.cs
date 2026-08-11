using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Thin seam over the Azure AI Search SDK so
/// <see cref="AzureDocumentIndexService"/> can be unit tested without a live
/// service. Implementations do no mapping or business logic — they pass calls
/// straight through to the SDK.
/// </summary>
public interface ISearchIndexGateway
{
    /// <summary>Creates <paramref name="index"/> or updates it in place.</summary>
    Task CreateOrUpdateIndexAsync(SearchIndex index, CancellationToken cancellationToken);

    /// <summary>Uploads (upserts) <paramref name="documents"/> into the index.</summary>
    Task UploadDocumentsAsync(IReadOnlyList<SearchDocument> documents, CancellationToken cancellationToken);

    /// <summary>Deletes the documents whose keys are in <paramref name="chunkIds"/>.</summary>
    Task DeleteDocumentsAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the chunk ids of every indexed document matching the OData
    /// <paramref name="filter"/>.
    /// </summary>
    Task<IReadOnlyList<string>> FindChunkIdsAsync(string filter, CancellationToken cancellationToken);
}
