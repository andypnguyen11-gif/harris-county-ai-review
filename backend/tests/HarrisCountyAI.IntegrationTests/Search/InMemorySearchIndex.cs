using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using HarrisCountyAI.Infrastructure.Azure.Search;

namespace HarrisCountyAI.IntegrationTests.Search;

/// <summary>
/// In-memory stand-in for the Azure AI Search index that honors the OData
/// filter shapes this codebase generates: conjunctions of
/// <c>field eq 'value'</c> clauses and <c>search.in(field, 'a,b', ',')</c>.
/// Implements both gateway seams, so the real
/// <see cref="AzureDocumentIndexService"/> and
/// <see cref="AzureRetrievalService"/> run end to end against it — including
/// the filters that keep the corpus and case documents apart.
/// </summary>
public sealed class InMemorySearchIndex : ISearchIndexGateway, ISearchQueryGateway
{
    private readonly Dictionary<string, SearchDocument> _documents = [];

    public IReadOnlyCollection<SearchDocument> Documents => _documents.Values;

    public List<ChunkSearchQuery> ExecutedQueries { get; } = [];

    public Task CreateOrUpdateIndexAsync(SearchIndex index, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task UploadDocumentsAsync(IReadOnlyList<SearchDocument> documents, CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            _documents[(string)document[SearchIndexDefinition.Fields.ChunkId]] = document;
        }

        return Task.CompletedTask;
    }

    public Task DeleteDocumentsAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken)
    {
        foreach (var chunkId in chunkIds)
        {
            _documents.Remove(chunkId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> FindChunkIdsAsync(string filter, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>(_documents.Values
            .Where(document => Matches(filter, document))
            .Select(document => (string)document[SearchIndexDefinition.Fields.ChunkId])
            .ToList());

    public Task<IReadOnlyList<ChunkSearchHit>> SearchAsync(ChunkSearchQuery query, CancellationToken cancellationToken)
    {
        ExecutedQueries.Add(query);

        // Every query path must filter; an unfiltered query would blend the
        // corpora, so the stand-in refuses it just as review would.
        if (string.IsNullOrWhiteSpace(query.Filter))
        {
            throw new InvalidOperationException("A query against the shared chunk index must carry a filter.");
        }

        var score = 1.0;
        IReadOnlyList<ChunkSearchHit> hits = _documents.Values
            .Where(document => Matches(query.Filter, document))
            .Take(query.Size)
            .Select(document => new ChunkSearchHit(document, score -= 0.01))
            .ToList();
        return Task.FromResult(hits);
    }

    /// <summary>Evaluates a conjunctive OData filter against one document.</summary>
    internal static bool Matches(string filter, SearchDocument document)
        => filter.Split(" and ", StringSplitOptions.TrimEntries).All(clause => MatchesClause(clause, document));

    private static bool MatchesClause(string clause, SearchDocument document)
    {
        if (clause.StartsWith("search.in(", StringComparison.Ordinal))
        {
            // search.in(field, 'a,b,c', ',')
            var inner = clause["search.in(".Length..clause.LastIndexOf(')')];
            var parts = inner.Split(',', 2, StringSplitOptions.TrimEntries);
            var field = parts[0];
            var listStart = parts[1].IndexOf('\'') + 1;
            var listEnd = parts[1].IndexOf('\'', listStart);
            var values = parts[1][listStart..listEnd].Split(',', StringSplitOptions.TrimEntries);
            return document.TryGetValue(field, out var actual)
                && actual is string text
                && values.Contains(text, StringComparer.Ordinal);
        }

        var tokens = clause.Split(" eq ", 2, StringSplitOptions.TrimEntries);
        if (tokens.Length != 2)
        {
            throw new InvalidOperationException($"Unsupported filter clause '{clause}'.");
        }

        var expected = tokens[1].Trim('\'').Replace("''", "'", StringComparison.Ordinal);
        return document.TryGetValue(tokens[0], out var value)
            && value is string stringValue
            && string.Equals(stringValue, expected, StringComparison.Ordinal);
    }
}
