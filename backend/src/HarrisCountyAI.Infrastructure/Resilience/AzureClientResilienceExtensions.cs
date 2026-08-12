using Azure.Core;

namespace HarrisCountyAI.Infrastructure.Resilience;

/// <summary>
/// Applies the shared retry and timeout budget to an Azure SDK client's
/// options.
/// </summary>
/// <remarks>
/// The SDK's own retry pipeline does the retrying rather than a policy of our
/// own, because it already knows which failures are worth repeating: it
/// retries 408, 429, and the 5xx family plus transport faults, honors
/// <c>Retry-After</c> when the service sends one, and leaves 4xx alone. Adding
/// a second retry layer on top would multiply attempts (3 retries becoming 9
/// requests) against a dependency that is already struggling.
/// </remarks>
public static class AzureClientResilienceExtensions
{
    /// <summary>
    /// Configures exponential backoff, the retry ceiling, and the per-attempt
    /// network timeout on <paramref name="options"/>, returning it for
    /// chaining.
    /// </summary>
    public static TOptions WithResilience<TOptions>(this TOptions options, AzureResilienceOptions resilience)
        where TOptions : ClientOptions
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resilience);

        options.Retry.Mode = RetryMode.Exponential;
        options.Retry.MaxRetries = Math.Max(0, resilience.MaxRetryAttempts);
        options.Retry.Delay = TimeSpan.FromMilliseconds(Math.Max(1, resilience.RetryBaseDelayMilliseconds));
        options.Retry.MaxDelay = TimeSpan.FromSeconds(Math.Max(1, resilience.MaxRetryDelaySeconds));
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(Math.Max(1, resilience.NetworkTimeoutSeconds));

        return options;
    }
}
