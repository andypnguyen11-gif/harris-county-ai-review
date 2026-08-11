using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.KnowledgeBase;
using HarrisCountyAI.Application.Search.Embeddings;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.KnowledgeBase.Ingestion;

/// <summary>In-memory <see cref="IKnowledgeDocumentRepository"/> that records the status persisted at each save.</summary>
public sealed class FakeKnowledgeDocumentRepository : IKnowledgeDocumentRepository
{
    private readonly Dictionary<Guid, KnowledgeDocument> _documents = [];

    public List<KnowledgeDocumentIngestionStatus> SavedStatuses { get; } = [];

    public void Add(KnowledgeDocument document) => _documents[document.Id] = document;

    public Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.TryGetValue(id, out var document) ? document : null);

    public Task<IReadOnlyList<KnowledgeDocument>> GetAllAsync(bool includeDeactivated = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<KnowledgeDocument>>(_documents.Values
            .Where(d => includeDeactivated || d.IngestionStatus != KnowledgeDocumentIngestionStatus.Deactivated)
            .OrderByDescending(d => d.CreatedAt)
            .ToList());

    public Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken = default)
    {
        _documents[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SavedStatuses.AddRange(_documents.Values.Select(d => d.IngestionStatus));
        return Task.CompletedTask;
    }
}

/// <summary>Configurable <see cref="IDocumentExtractionService"/> serving canned pages.</summary>
public sealed class FakeKnowledgeExtractionService : IDocumentExtractionService
{
    public Exception? ExtractException { get; set; }

    public IReadOnlyList<ExtractedPage> Pages { get; set; } =
    [
        new ExtractedPage
        {
            PageNumber = 1,
            Text = "1. Elevation Requirements\r\nAll structures must be elevated at least "
                + "one foot above the base flood elevation shown on the effective FIRM.",
        },
        new ExtractedPage
        {
            PageNumber = 2,
            Text = "2. Required Submittals\nA sealed elevation certificate and a site plan "
                + "showing the floodplain boundary are required with every application.",
        },
    ];

    public string RawText { get; set; } = string.Empty;

    public Guid? LastDocumentId { get; private set; }

    public Task<ExtractedDocument> ExtractAsync(Guid documentId, Stream content, CancellationToken cancellationToken)
    {
        LastDocumentId = documentId;

        if (ExtractException is not null)
        {
            throw ExtractException;
        }

        return Task.FromResult(new ExtractedDocument
        {
            DocumentId = documentId,
            Pages = Pages,
            RawText = RawText.Length > 0 ? RawText : string.Join("\n", Pages.Select(p => p.Text)),
            ModelId = "fake-model",
            ExtractedAt = DateTime.UtcNow,
        });
    }
}

/// <summary>
/// Deterministic <see cref="IEmbeddingService"/>: the vector for input
/// <c>i</c> has <c>i + 1</c> as its first component, so tests can assert
/// embeddings were matched to the right chunk.
/// </summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    public Exception? EmbedException { get; set; }

    public int Dimensions { get; set; } = 1536;

    /// <summary>Returns results in reverse order to exercise index-based matching.</summary>
    public bool ReverseResultOrder { get; set; }

    /// <summary>When set, returns only this many results to simulate a provider miscount.</summary>
    public int? ResultCountOverride { get; set; }

    public List<IReadOnlyList<string>> Requests { get; } = [];

    public Task<IReadOnlyList<EmbeddingResult>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        Requests.Add(inputs.ToList());

        if (EmbedException is not null)
        {
            throw EmbedException;
        }

        var results = inputs.Select((_, index) => new EmbeddingResult(MakeVector(index), index, "fake-embedding-model")).ToList();

        if (ReverseResultOrder)
        {
            results.Reverse();
        }

        if (ResultCountOverride is { } count)
        {
            results = results.Take(count).ToList();
        }

        return Task.FromResult<IReadOnlyList<EmbeddingResult>>(results);
    }

    private float[] MakeVector(int index)
    {
        var vector = new float[Dimensions];
        vector[0] = index + 1;
        return vector;
    }
}

/// <summary>In-memory <see cref="IDocumentIndexService"/> that records the order of index operations.</summary>
public sealed class FakeDocumentIndexService : IDocumentIndexService
{
    public Exception? EnsureIndexException { get; set; }

    public Exception? IndexException { get; set; }

    public Exception? DeleteException { get; set; }

    /// <summary>Operation log: "EnsureIndex", "Delete:{documentId}", "Index:{chunkCount}".</summary>
    public List<string> Operations { get; } = [];

    public List<IReadOnlyList<IndexableChunk>> IndexedBatches { get; } = [];

    public Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        Operations.Add("EnsureIndex");

        if (EnsureIndexException is not null)
        {
            throw EnsureIndexException;
        }

        return Task.CompletedTask;
    }

    public Task IndexAsync(IReadOnlyList<IndexableChunk> chunks, CancellationToken cancellationToken = default)
    {
        Operations.Add($"Index:{chunks.Count}");

        if (IndexException is not null)
        {
            throw IndexException;
        }

        IndexedBatches.Add(chunks);
        return Task.CompletedTask;
    }

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Operations.Add($"Delete:{documentId}");

        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        return Task.CompletedTask;
    }
}
