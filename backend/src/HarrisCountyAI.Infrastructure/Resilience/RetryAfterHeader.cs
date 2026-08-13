using System.ClientModel;
using System.Globalization;
using Azure;

namespace HarrisCountyAI.Infrastructure.Resilience;

/// <summary>
/// Reads the <c>Retry-After</c> hint a throttled dependency sends back.
/// </summary>
/// <remarks>
/// When Azure OpenAI rejects a request with 429 it also says how many seconds
/// are left in the rate-limit window. That number is the only thing we actually
/// know about when a retry could succeed; exponential backoff is a guess, and a
/// guess that lands back inside the same window spends an attempt for nothing.
/// A caller that retries four times in two seconds against a sixty-second
/// window reports a permanent failure for a condition that was temporary.
/// <para>
/// The Azure SDK retry policies read this header themselves. Any caller that
/// turns the SDK policy off to own retries — see
/// <c>AzureOpenAIEmbeddingBatchClient</c> — has to read it too, or it retries
/// strictly worse than the policy it replaced.
/// </para>
/// </remarks>
public static class RetryAfterHeader
{
    private const string HeaderName = "Retry-After";

    /// <summary>
    /// How long the dependency asked us to wait, or <see langword="null"/> when
    /// it sent no hint, sent one we cannot read, or the failure carries no
    /// response at all.
    /// </summary>
    public static TimeSpan? Read(Exception? exception) => Read(exception, DateTimeOffset.UtcNow);

    /// <summary>
    /// Test seam: <paramref name="now"/> is the reference point the absolute
    /// (HTTP-date) form of the header is measured against.
    /// </summary>
    internal static TimeSpan? Read(Exception? exception, DateTimeOffset now) => exception switch
    {
        null => null,

        // The two shapes an Azure failure arrives in: System.ClientModel for the
        // OpenAI clients, Azure.Core for Search, Blob, and Document Intelligence.
        ClientResultException failure => Parse(HeaderValue(failure), now),
        RequestFailedException failure => Parse(HeaderValue(failure), now),

        AggregateException aggregate => aggregate.InnerExceptions
            .Select(inner => Read(inner, now))
            .FirstOrDefault(value => value is not null),

        _ => Read(exception.InnerException, now),
    };

    private static string? HeaderValue(ClientResultException failure) =>
        failure.GetRawResponse() is { } response && response.Headers.TryGetValue(HeaderName, out var value)
            ? value
            : null;

    private static string? HeaderValue(RequestFailedException failure) =>
        failure.GetRawResponse() is { } response && response.Headers.TryGetValue(HeaderName, out var value)
            ? value
            : null;

    /// <summary>
    /// Parses either RFC 9110 form of the header. A hint that has already passed
    /// becomes <see cref="TimeSpan.Zero"/> rather than a negative delay, so a
    /// caller can treat any returned value as a floor to wait for.
    /// </summary>
    private static TimeSpan? Parse(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        // Delta-seconds: the form Azure OpenAI uses. RFC 9110 specifies a whole
        // number, but some services send a fraction, so decimals are accepted.
        // AllowDecimalPoint excludes a leading sign, so "-5" falls through to
        // the date branch and is then rejected as unreadable.
        if (double.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }

        // HTTP-date: the absolute form, e.g. "Wed, 21 Oct 2015 07:28:00 GMT".
        if (DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out var resumeAt))
        {
            var delay = resumeAt - now;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
