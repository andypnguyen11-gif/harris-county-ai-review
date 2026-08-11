using System.Text.Json;

namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// Maps the source numbers a model cited back to the passages actually shown
/// to it. Shared by the single-scope and dual-source answering paths so the
/// resolution rules — and the fail-closed handling of numbers the model
/// invented — live in exactly one place.
/// </summary>
internal static class CitationResolver
{
    /// <summary>
    /// Resolves the <c>citations</c> array of a model response into citations,
    /// ignoring duplicates, non-numbers, and numbers outside 1..N. Each
    /// citation carries the corpus its passage was retrieved from, taken from
    /// <paramref name="sources"/> rather than from anything the model said.
    /// </summary>
    public static IReadOnlyList<Citation> Resolve(JsonElement root, IReadOnlyList<EvidenceSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (!root.TryGetProperty("citations", out var citationsElement)
            || citationsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var citations = new List<Citation>();
        var seen = new HashSet<int>();
        foreach (var element in citationsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Number
                || !element.TryGetInt32(out var number)
                || number < 1
                || number > sources.Count
                || !seen.Add(number))
            {
                continue;
            }

            var (source, chunk) = sources[number - 1];
            citations.Add(new Citation
            {
                Number = number,
                Source = source,
                ChunkId = chunk.ChunkId,
                DocumentId = chunk.DocumentId,
                Title = chunk.Title,
                Section = chunk.Section,
                Page = chunk.Page,
                SourceUrl = chunk.SourceUrl,
            });
        }

        return citations;
    }
}
