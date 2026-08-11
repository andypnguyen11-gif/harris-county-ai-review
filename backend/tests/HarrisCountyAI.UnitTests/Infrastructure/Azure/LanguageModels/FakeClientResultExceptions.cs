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
        new($"Simulated failure with status {status}.", new FakePipelineResponse(status));

    private sealed class FakePipelineResponse(int status) : PipelineResponse
    {
        public override int Status { get; } = status;

        public override string ReasonPhrase => "Simulated";

        public override Stream? ContentStream { get; set; } = new MemoryStream();

        public override BinaryData Content { get; } = BinaryData.FromString(string.Empty);

        protected override PipelineResponseHeaders HeadersCore { get; } = new FakeHeaders();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            new(Content);

        public override void Dispose()
        {
        }
    }

    private sealed class FakeHeaders : PipelineResponseHeaders
    {
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();

        public override bool TryGetValue(string name, out string? value)
        {
            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }
}
