namespace HarrisCountyAI.Application.Validation.Comparison;

/// <summary>
/// Compares what a case submitted against what Harris County requires, one
/// requirement at a time, as a repeatable service rather than a chat exchange.
/// </summary>
public interface IRequirementComparisonService
{
    /// <summary>
    /// Produces one <see cref="RequirementComparisonResult"/> per applicable
    /// requirement, in catalog order.
    /// </summary>
    Task<IReadOnlyList<RequirementComparisonResult>> CompareAsync(
        RequirementComparisonRequest request,
        CancellationToken cancellationToken = default);
}
