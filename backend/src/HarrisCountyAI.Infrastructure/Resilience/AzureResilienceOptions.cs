namespace HarrisCountyAI.Infrastructure.Resilience;

/// <summary>
/// Retry and timeout budget applied to every Azure SDK client the application
/// creates. One shared budget rather than per-service knobs: the failure modes
/// are the same everywhere (throttling, a bad node, a network blip), and a
/// single number is one an operator can actually reason about.
/// </summary>
public sealed class AzureResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>
    /// Retries attempted after the first try. Deliberately small: the request
    /// budget is shared with a user waiting on an HTTP response, and a long
    /// retry chain against a struggling dependency adds load without adding
    /// success.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay before the first retry. Subsequent retries back off
    /// exponentially from here, with jitter applied by the SDK.
    /// </summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    /// <summary>Ceiling for the exponential backoff between retries.</summary>
    public int MaxRetryDelaySeconds { get; set; } = 10;

    /// <summary>
    /// Per-attempt network timeout. Bounds a single round trip, not the whole
    /// retried operation; services with their own longer budget (document
    /// extraction, chat completions) keep it through their own options.
    /// </summary>
    public int NetworkTimeoutSeconds { get; set; } = 30;
}
