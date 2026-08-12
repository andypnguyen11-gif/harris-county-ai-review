using HarrisCountyAI.Application.Documents.Extraction;

namespace HarrisCountyAI.IntegrationTests.EndToEnd;

/// <summary>
/// Stands in for Azure AI Document Intelligence so the end-to-end suite never
/// calls a real extraction service. Each document is scripted by id before it
/// is processed; an unscripted document is an error rather than a silent empty
/// result, so a test can never pass because extraction quietly returned
/// nothing.
/// </summary>
public sealed class ScriptedExtractionService : IDocumentExtractionService
{
    private readonly Dictionary<Guid, Func<Guid, ExtractedDocument>> _scripted = [];

    /// <summary>Document ids extraction was asked for, in call order.</summary>
    public List<Guid> Requests { get; } = [];

    /// <summary>When set, extraction throws this instead of returning a result — a malformed or unreadable file.</summary>
    public Exception? ExtractException { get; set; }

    /// <summary>Scripts the extraction result for one document.</summary>
    public void Script(Guid documentId, Func<Guid, ExtractedDocument> extracted) =>
        _scripted[documentId] = extracted;

    public Task<ExtractedDocument> ExtractAsync(Guid documentId, Stream content, CancellationToken cancellationToken)
    {
        Requests.Add(documentId);

        if (ExtractException is not null)
        {
            throw ExtractException;
        }

        if (!_scripted.TryGetValue(documentId, out var extracted))
        {
            throw new InvalidOperationException(
                $"No extraction result was scripted for document '{documentId}'.");
        }

        return Task.FromResult(extracted(documentId));
    }
}
