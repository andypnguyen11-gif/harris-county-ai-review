using System.ClientModel;
using System.ClientModel.Primitives;

namespace HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Builds <see cref="ClientResultException"/> instances carrying a specific HTTP status,
/// matching what the Azure OpenAI SDK throws for failed requests.
/// </summary>
internal static class FakeClientResultExceptions
{
    public static ClientResultException WithStatus(int status) =>
        new($"Simulated failure with status {status}.", new FakePipelineResponse(status, null));

    /// <summary>
    /// A failure that also carries a <c>Retry-After</c> header, as a throttled
    /// Azure OpenAI deployment sends with a 429.
    /// </summary>
    public static ClientResultException WithRetryAfter(int status, string retryAfter) =>
        new(
            $"Simulated failure with status {status}.",
            new FakePipelineResponse(status, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Retry-After"] = retryAfter,
            }));

    private sealed class FakePipelineResponse(int status, IReadOnlyDictionary<string, string>? headers)
        : PipelineResponse
    {
        public override int Status { get; } = status;

        public override string ReasonPhrase => "Simulated";

        public override Stream? ContentStream { get; set; } = new MemoryStream();

        public override BinaryData Content { get; } = BinaryData.FromString(string.Empty);

        protected override PipelineResponseHeaders HeadersCore { get; } = new FakeHeaders(headers);

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            new(Content);

        public override void Dispose()
        {
        }
    }

    private sealed class FakeHeaders(IReadOnlyDictionary<string, string>? headers) : PipelineResponseHeaders
    {
        private readonly IReadOnlyDictionary<string, string> _headers =
            headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _headers.GetEnumerator();

        public override bool TryGetValue(string name, out string? value)
        {
            if (_headers.TryGetValue(name, out var found))
            {
                value = found;
                return true;
            }

            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            if (_headers.TryGetValue(name, out var found))
            {
                values = [found];
                return true;
            }

            values = null;
            return false;
        }
    }
}
