using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Validation.Comparison;

/// <summary>
/// What the applicant actually submitted that bears on a requirement — the
/// document, and where in it the deterministic check found (or failed to find)
/// what the requirement asks for.
/// </summary>
/// <remarks>
/// Always drawn from this case's own extracted documents, never from the
/// county corpus. It records where a reviewer should look to confirm the
/// finding, which is why it carries page and field names rather than only a
/// verdict.
/// </remarks>
public sealed record SubmissionEvidence
{
    /// <summary>The normalized document the evidence came from.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Category of that document.</summary>
    public required DocumentType DocumentType { get; init; }

    /// <summary>1-based page the evidence appears on, when resolvable.</summary>
    public int? Page { get; init; }

    /// <summary>Name of the field the evidence came from, when the evidence is a field.</summary>
    public string? FieldName { get; init; }

    /// <summary>The value read from that field, when one was present.</summary>
    public string? ExtractedValue { get; init; }
}
