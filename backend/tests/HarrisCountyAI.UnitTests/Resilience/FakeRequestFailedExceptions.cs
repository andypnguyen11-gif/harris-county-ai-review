using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;

namespace HarrisCountyAI.UnitTests.Resilience;

/// <summary>
/// Builds <see cref="RequestFailedException"/> instances backed by a real
/// <see cref="Response"/>, so header-reading code can be exercised. The
/// status-only constructor on <see cref="RequestFailedException"/> leaves
/// <see cref="RequestFailedException.GetRawResponse"/> null, which cannot
/// distinguish "no header sent" from "no response at all".
/// </summary>
internal static class FakeRequestFailedExceptions
{
    public static RequestFailedException WithRetryAfter(int status, string retryAfter) =>
        new(new FakeResponse(status, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Retry-After"] = retryAfter,
        }));

    public static RequestFailedException WithoutHeaders(int status) =>
        new(new FakeResponse(status, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

    private sealed class FakeResponse(int status, IReadOnlyDictionary<string, string> headers) : Response
    {
        public override int Status { get; } = status;

        public override string ReasonPhrase => "Simulated";

        public override Stream? ContentStream { get; set; } = new MemoryStream();

        public override string ClientRequestId { get; set; } = "simulated";

        public override void Dispose() => ContentStream?.Dispose();

        protected override bool ContainsHeader(string name) => headers.ContainsKey(name);

        protected override IEnumerable<HttpHeader> EnumerateHeaders() =>
            headers.Select(header => new HttpHeader(header.Key, header.Value));

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value) =>
            headers.TryGetValue(name, out value);

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            if (headers.TryGetValue(name, out var found))
            {
                values = [found];
                return true;
            }

            values = null;
            return false;
        }
    }
}
