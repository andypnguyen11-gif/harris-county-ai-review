namespace HarrisCountyAI.Domain.Enums;

/// <summary>Outcome of evaluating a single validation requirement.</summary>
public enum ValidationStatus
{
    /// <summary>The requirement is satisfied.</summary>
    Complete,

    /// <summary>A required document, field, signature, or selection is absent.</summary>
    Missing,

    /// <summary>The item is present but its value fails a deterministic check (e.g. an unparseable or future date).</summary>
    Invalid,

    /// <summary>The item is present but appears incomplete; a closer look is warranted.</summary>
    PotentiallyIncomplete,

    /// <summary>The check requires human judgment that deterministic rules cannot provide.</summary>
    NeedsHumanReview,

    /// <summary>The rule could not run to a conclusion (e.g. the underlying document is absent or the rule failed).</summary>
    UnableToDetermine,
}
