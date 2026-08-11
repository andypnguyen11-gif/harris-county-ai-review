using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Entities;

/// <summary>
/// Vendor-neutral, normalized snapshot of an extracted document: the shape the
/// rest of the system (validation, search, question answering) consumes so it
/// never depends on Azure Document Intelligence output directly.
/// </summary>
/// <remarks>
/// Unlike the factory-style aggregates (<see cref="Case"/>,
/// <see cref="Document"/>), this is a mutable data snapshot with public
/// setters: it has no invariants to protect or state transitions to guard —
/// it is written once by the normalization service and read thereafter, and
/// is replaced (not edited) when a document is reprocessed.
/// </remarks>
public class NormalizedDocument
{
    public Guid Id { get; set; }

    /// <summary>The uploaded <see cref="Document"/> this snapshot was produced from.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>The case the source document belongs to, denormalized for direct lookups.</summary>
    public Guid CaseId { get; set; }

    public DocumentType DocumentType { get; set; }

    /// <summary>Full text of the document in reading order.</summary>
    public string RawText { get; set; } = string.Empty;

    public List<DocumentPage> Pages { get; set; } = [];

    public List<DocumentField> Fields { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}
