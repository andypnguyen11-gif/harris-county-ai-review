namespace HarrisCountyAI.Domain.Enums;

/// <summary>Lifecycle state of a document review case.</summary>
public enum CaseStatus
{
    New,
    Processing,
    ReadyForReview,
    InReview,
    Completed,
}
