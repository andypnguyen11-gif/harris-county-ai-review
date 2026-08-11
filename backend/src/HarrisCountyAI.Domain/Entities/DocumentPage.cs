namespace HarrisCountyAI.Domain.Entities;

/// <summary>
/// A single page of a <see cref="NormalizedDocument"/>. A mutable data
/// snapshot for the same reason as its owner — see the remarks on
/// <see cref="NormalizedDocument"/>.
/// </summary>
public class DocumentPage
{
    public Guid Id { get; set; }

    /// <summary>1-based page number.</summary>
    public int PageNumber { get; set; }

    /// <summary>Text content of the page in reading order.</summary>
    public string Text { get; set; } = string.Empty;
}
