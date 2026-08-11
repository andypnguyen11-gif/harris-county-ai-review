using HarrisCountyAI.Application.Search.Chunking;

namespace HarrisCountyAI.Application.Search.Indexing;

/// <summary>
/// A <see cref="DocumentChunk"/> enriched with the source metadata and the
/// precomputed embedding it needs to be written to the search index. The
/// embedding is supplied by the caller (1536 dimensions,
/// text-embedding-3-small); indexing has no dependency on the embedding
/// service itself.
/// </summary>
public sealed record IndexableChunk
{
    /// <summary>Unique chunk key: <c>"{documentId:N}-{sequence:D4}"</c>.</summary>
    public required string ChunkId { get; init; }

    /// <summary>Parent document identifier.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Zero-based position of the chunk within its document.</summary>
    public required int Sequence { get; init; }

    /// <summary>Chunk text content.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Chunk origin: <see cref="IndexSourceTypes.KnowledgeBase"/> or
    /// <see cref="IndexSourceTypes.CaseDocument"/>. Together with
    /// <see cref="CaseId"/> this keeps case evidence and the county reference
    /// corpus filterable apart inside the shared index.
    /// </summary>
    public required string SourceType { get; init; }

    /// <summary>Title of the source document.</summary>
    public required string Title { get; init; }

    /// <summary>Section heading the chunk was taken from, if known.</summary>
    public string? Section { get; init; }

    /// <summary>Page the chunk starts on, if known.</summary>
    public int? PageNumber { get; init; }

    /// <summary>Owning county department, for reference-corpus documents.</summary>
    public string? Department { get; init; }

    /// <summary>Permit type the document applies to (e.g. floodplain).</summary>
    public string? PermitType { get; init; }

    /// <summary>Document category (e.g. regulation, form, checklist).</summary>
    public string? DocumentType { get; init; }

    /// <summary>Date the source document took effect, if known.</summary>
    public DateTimeOffset? EffectiveDate { get; init; }

    /// <summary>Public URL of the source document, if one exists.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// Case the chunk belongs to. Null for knowledge-base documents; required
    /// in spirit for case documents so queries can scope to a single case.
    /// </summary>
    public Guid? CaseId { get; init; }

    /// <summary>Precomputed embedding vector (1536 dimensions).</summary>
    public required float[] Embedding { get; init; }

    /// <summary>
    /// Builds an <see cref="IndexableChunk"/> from a chunker-produced
    /// <see cref="DocumentChunk"/>, its precomputed embedding, and source
    /// metadata.
    /// </summary>
    public static IndexableChunk FromChunk(
        DocumentChunk chunk,
        float[] embedding,
        string sourceType,
        string title,
        string? department = null,
        string? permitType = null,
        string? documentType = null,
        DateTimeOffset? effectiveDate = null,
        string? sourceUrl = null,
        Guid? caseId = null) => new()
        {
            ChunkId = chunk.ChunkId,
            DocumentId = chunk.DocumentId,
            Sequence = chunk.Sequence,
            Text = chunk.Text,
            Section = chunk.Section,
            PageNumber = chunk.PageNumber,
            SourceType = sourceType,
            Title = title,
            Department = department,
            PermitType = permitType,
            DocumentType = documentType,
            EffectiveDate = effectiveDate,
            SourceUrl = sourceUrl,
            CaseId = caseId,
            Embedding = embedding,
        };
}
