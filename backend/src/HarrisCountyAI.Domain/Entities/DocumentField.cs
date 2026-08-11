using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Entities;

/// <summary>
/// A normalized field of a <see cref="NormalizedDocument"/> — a labeled value,
/// checkbox, or signature recognized on the document. A mutable data snapshot
/// for the same reason as its owner — see the remarks on
/// <see cref="NormalizedDocument"/>.
/// </summary>
public class DocumentField
{
    public Guid Id { get; set; }

    /// <summary>Normalized field name: trimmed, lower-cased, no trailing colon.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The recognized value as printed, or null when the field was blank.</summary>
    public string? Value { get; set; }

    public FieldKind Kind { get; set; }

    /// <summary>Whether the checkbox is checked; null for non-checkbox fields.</summary>
    public bool? IsChecked { get; set; }

    /// <summary>Whether the signature appears present; null for non-signature fields.</summary>
    public bool? IsSigned { get; set; }

    /// <summary>Recognition confidence between 0 and 1, when reported.</summary>
    public double? Confidence { get; set; }

    /// <summary>1-based page number the field appears on, when resolvable.</summary>
    public int? PageNumber { get; set; }
}
