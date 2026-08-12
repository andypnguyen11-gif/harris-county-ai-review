using System.Text;
using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// A deterministic offline <see cref="IRetrievalService"/> over the committed
/// fixture corpus, scoring passages with a small BM25-style lexical ranker.
/// </summary>
/// <remarks>
/// This is not a stand-in for Azure AI Search and is not meant to be: hybrid
/// search, embeddings, and semantic reranking all beat plain lexical matching,
/// which is exactly why the fixture baseline is labeled
/// <see cref="Application.Evaluation.EvaluationRunType.Fixture"/> and must never
/// be read as a measurement of production retrieval. What it does give is a run
/// that is free, offline, byte-reproducible, and sensitive enough that a change
/// to the dataset, the matcher, or the metrics shows up as a baseline diff.
/// </remarks>
public sealed class FixtureCorpusRetrievalService : IRetrievalService
{
    /// <summary>BM25 term-frequency saturation.</summary>
    private const double K1 = 1.2;

    /// <summary>BM25 length normalization.</summary>
    private const double B = 0.75;

    /// <summary>
    /// Extra weight for a term that also appears in the title or section
    /// heading, standing in for the field boosting a real search index applies.
    /// </summary>
    private const double HeadingBoost = 1.6;

    /// <summary>
    /// Words carrying no discriminating signal in this corpus. Kept tiny and
    /// explicit rather than pulled from a library so the ranking stays
    /// inspectable.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "about", "after", "all", "an", "and", "any", "anything", "are", "as", "at", "be",
        "before", "being", "but", "by", "can", "did", "do", "does", "for", "from", "get", "goes",
        "has", "have", "how", "i", "if", "in", "into", "is", "it", "its", "me", "must", "my", "need",
        "not", "of", "on", "only", "or", "out", "part", "says", "should", "so", "some", "than",
        "that", "the", "their", "them", "then", "there", "these", "they", "this", "to", "up", "use",
        "used", "was", "way", "were", "what", "when", "where", "which", "who", "will", "with",
        "would", "you", "your",
    };

    private readonly IReadOnlyList<IndexedPassage> _passages;
    private readonly Dictionary<string, int> _documentFrequency;
    private readonly double _averageLength;

    public FixtureCorpusRetrievalService(FixtureCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        _passages = [.. corpus.Passages.Select(IndexedPassage.Create)];
        _averageLength = _passages.Average(passage => (double)passage.Length);
        _documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var passage in _passages)
        {
            foreach (var term in passage.TermFrequencies.Keys)
            {
                _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
            }
        }
    }

    /// <summary>Builds a retrieval service over the committed fixture corpus.</summary>
    public static FixtureCorpusRetrievalService FromCommittedCorpus() => new(FixtureCorpus.Load());

    public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Scope != SourceType.County)
        {
            throw new NotSupportedException(
                "The fixture corpus only stands in for the Harris County reference corpus.");
        }

        var queryTerms = Tokenize(request.Query).Distinct(StringComparer.Ordinal).ToList();
        var topK = Math.Clamp(request.TopK ?? RetrievalRequest.DefaultTopK, 1, _passages.Count);

        var ranked = _passages
            .Select(passage => (Passage: passage, Score: Score(passage, queryTerms)))
            .Where(candidate => candidate.Score > 0)
            // Ties broken by corpus order so the ranking — and therefore every
            // committed baseline — is byte-identical on every machine.
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Passage.Ordinal)
            .Take(topK)
            .Select(candidate => candidate.Passage.ToChunk(candidate.Score))
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(ranked);
    }

    private double Score(IndexedPassage passage, IReadOnlyList<string> queryTerms)
    {
        var score = 0d;
        foreach (var term in queryTerms)
        {
            if (!passage.TermFrequencies.TryGetValue(term, out var frequency))
            {
                continue;
            }

            var documentFrequency = _documentFrequency.GetValueOrDefault(term);
            var idf = Math.Log(1 + ((_passages.Count - documentFrequency + 0.5) / (documentFrequency + 0.5)));
            var normalized = frequency
                * (K1 + 1)
                / (frequency + (K1 * (1 - B + (B * passage.Length / _averageLength))));
            var boost = passage.HeadingTerms.Contains(term) ? HeadingBoost : 1d;
            score += idf * normalized * boost;
        }

        return Math.Round(score, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Lowercases, splits on non-alphanumerics, and drops stop words and single characters.</summary>
    internal static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var builder = new StringBuilder();
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length > 0)
            {
                var token = builder.ToString();
                builder.Clear();
                if (token.Length > 1 && !StopWords.Contains(token))
                {
                    yield return token;
                }
            }
        }

        if (builder.Length > 0)
        {
            var last = builder.ToString();
            if (last.Length > 1 && !StopWords.Contains(last))
            {
                yield return last;
            }
        }
    }

    private sealed record IndexedPassage(
        int Ordinal,
        FixturePassage Source,
        IReadOnlyDictionary<string, int> TermFrequencies,
        IReadOnlySet<string> HeadingTerms,
        int Length)
    {
        public static IndexedPassage Create(FixturePassage passage, int ordinal)
        {
            var headingTerms = Tokenize($"{passage.Title} {passage.Section}").ToHashSet(StringComparer.Ordinal);
            var tokens = Tokenize($"{passage.Title} {passage.Section} {passage.Text}").ToList();
            var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var token in tokens)
            {
                frequencies[token] = frequencies.GetValueOrDefault(token) + 1;
            }

            return new IndexedPassage(ordinal, passage, frequencies, headingTerms, tokens.Count);
        }

        public RetrievedChunk ToChunk(double score) => new()
        {
            ChunkId = Source.Id,
            DocumentId = DeterministicDocumentId(Source.Title),
            Text = Source.Text,
            Title = Source.Title,
            Section = Source.Section,
            Page = Source.Page,
            DocumentType = "Fixture",
            Score = score,
        };

        /// <summary>
        /// Derives a stable document id from the title so repeated fixture runs
        /// produce identical result files.
        /// </summary>
        private static Guid DeterministicDocumentId(string title)
        {
            var digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(title));
            return new Guid(digest.AsSpan(0, 16));
        }
    }
}
