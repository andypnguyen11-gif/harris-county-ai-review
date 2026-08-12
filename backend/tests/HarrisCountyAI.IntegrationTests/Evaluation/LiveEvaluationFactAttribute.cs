namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// A fact that only runs when the caller has explicitly opted into a live
/// evaluation run: <c>RUN_EVALUATION=1</c> plus the Azure configuration the run
/// needs. Skips cleanly otherwise.
/// </summary>
/// <remarks>
/// Live evaluation runs issue one embedding call and one search query per
/// dataset question — and, for generation and judge runs, one model completion
/// per question on top of that. That costs real money on a metered Azure
/// subscription, so it is never the default: a plain <c>dotnet test</c> runs the
/// deterministic fixture harness and skips everything marked with this
/// attribute. Follows the same shape as <c>AzureSearchFactAttribute</c>.
/// </remarks>
public sealed class LiveEvaluationFactAttribute : FactAttribute
{
    /// <summary>Environment variable that opts a machine into billable evaluation runs.</summary>
    public const string OptInVariable = "RUN_EVALUATION";

    /// <param name="requires">
    /// Names of the additional environment settings the test needs (for example
    /// <c>Search__Endpoint</c>); the test is skipped when any is missing.
    /// </param>
    public LiveEvaluationFactAttribute(params string[] requires)
    {
        if (!EvaluationWorkspace.IsEnabled(Environment.GetEnvironmentVariable(OptInVariable)))
        {
            Skip = $"{OptInVariable} is not set; skipping the billable live evaluation run.";
            return;
        }

        var missing = requires
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToList();
        if (missing.Count > 0)
        {
            Skip = $"{string.Join(", ", missing)} not configured; skipping the live evaluation run.";
        }
    }
}
