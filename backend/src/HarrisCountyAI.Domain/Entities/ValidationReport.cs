using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Entities;

/// <summary>
/// Persisted snapshot of one validation run over a case's submitted documents:
/// the workflow that was applied and one <see cref="ValidationReportItem"/> per
/// rule that executed. Each run produces a new report; the newest report for a
/// case is the current one.
/// </summary>
/// <remarks>
/// Like <see cref="NormalizedDocument"/>, this is a mutable data snapshot with
/// public setters: it is written once when a validation run completes and read
/// thereafter, never edited.
/// </remarks>
public class ValidationReport
{
    public Guid Id { get; set; }

    /// <summary>The case whose submission package was validated.</summary>
    public Guid CaseId { get; set; }

    /// <summary>The workflow whose rule set produced this report.</summary>
    public WorkflowType WorkflowType { get; set; }

    /// <summary>Per-rule outcomes, in the order the workflow ran its rules.</summary>
    public List<ValidationReportItem> Items { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}
