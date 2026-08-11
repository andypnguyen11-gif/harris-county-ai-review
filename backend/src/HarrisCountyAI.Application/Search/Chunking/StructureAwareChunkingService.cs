using System.Text;
using System.Text.RegularExpressions;

namespace HarrisCountyAI.Application.Search.Chunking;

/// <summary>
/// Chunks extracted document text along its structure instead of at fixed
/// intervals. Headings start new sections; a section that fits within the max
/// chunk size becomes a single chunk. Oversized sections are split at
/// paragraph boundaries, then sentence boundaries, with a configurable
/// overlap carried between the resulting chunks so no split loses context.
/// </summary>
public sealed class StructureAwareChunkingService : IDocumentChunkingService
{
    private const int MaxHeadingLength = 100;
    private const int MaxShortLineHeadingLength = 80;
    private const int MaxShortLineHeadingWords = 10;

    // "1. Introduction", "2) Applicability", "3.1 Elevation Data", "4.2.1 Fill"
    private static readonly Regex NumberedHeadingPattern = new(
        @"^(?:\d+(?:\.\d+)+\.?|\d+[.)])\s+\S", RegexOptions.Compiled);

    private static readonly Regex SentenceBoundaryPattern = new(
        @"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly ChunkingOptions _options;

    public StructureAwareChunkingService()
        : this(new ChunkingOptions())
    {
    }

    public StructureAwareChunkingService(ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public IReadOnlyList<DocumentChunk> ChunkDocument(ChunkingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chunks = new List<DocumentChunk>();

        foreach (var section in BuildSections(request.Pages ?? []))
        {
            foreach (var piece in SplitSection(section))
            {
                var sequence = chunks.Count;
                chunks.Add(new DocumentChunk
                {
                    ChunkId = $"{request.DocumentId:N}-{sequence:D4}",
                    DocumentId = request.DocumentId,
                    Sequence = sequence,
                    Text = piece.Text,
                    Title = request.Title,
                    Section = section.Heading,
                    PageNumber = piece.PageNumber,
                });
            }
        }

        return chunks;
    }

    private readonly record struct TextUnit(string Text, int? PageNumber);

    private sealed class Section
    {
        public string? Heading { get; set; }

        public int? HeadingPage { get; set; }

        public List<TextUnit> Paragraphs { get; } = [];

        public bool IsEmpty => Heading is null && Paragraphs.Count == 0;
    }

    private static List<Section> BuildSections(IReadOnlyList<ChunkingPage> pages)
    {
        var sections = new List<Section>();
        var current = new Section();

        void CloseSection()
        {
            if (!current.IsEmpty)
            {
                sections.Add(current);
            }

            current = new Section();
        }

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page?.Text))
            {
                continue;
            }

            var lines = page.Text.Replace("\r\n", "\n").Split('\n');
            var paragraph = new StringBuilder();
            var previousBlank = true;

            void FlushParagraph()
            {
                if (paragraph.Length > 0)
                {
                    var text = paragraph.ToString().Trim();
                    if (text.Length > 0)
                    {
                        current.Paragraphs.Add(new TextUnit(text, page.PageNumber));
                    }

                    paragraph.Clear();
                }
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();

                if (trimmed.Length == 0)
                {
                    FlushParagraph();
                    previousBlank = true;
                    continue;
                }

                if (IsHeading(trimmed, previousBlank, HasFollowingContent(lines, i)))
                {
                    FlushParagraph();

                    if (current.Heading is not null && current.Paragraphs.Count == 0)
                    {
                        // Multi-line heading: fold into the pending section label.
                        current.Heading += " " + trimmed;
                    }
                    else
                    {
                        CloseSection();
                        current.Heading = trimmed;
                        current.HeadingPage = page.PageNumber;
                    }
                }
                else
                {
                    if (paragraph.Length > 0)
                    {
                        paragraph.Append('\n');
                    }

                    paragraph.Append(trimmed);
                }

                previousBlank = false;
            }

            FlushParagraph();
        }

        CloseSection();
        return sections;
    }

    private static bool HasFollowingContent(string[] lines, int index)
    {
        for (var i = index + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHeading(string trimmed, bool previousBlank, bool hasFollowingContent)
    {
        if (trimmed.Length > MaxHeadingLength)
        {
            return false;
        }

        if (NumberedHeadingPattern.IsMatch(trimmed))
        {
            return true;
        }

        if (IsAllCapsHeading(trimmed))
        {
            return true;
        }

        // A short standalone line that does not end like a sentence and is
        // followed by more content reads as a title-case heading.
        if (!previousBlank || !hasFollowingContent)
        {
            return false;
        }

        if (trimmed.Length > MaxShortLineHeadingLength || !trimmed.Any(char.IsLetter))
        {
            return false;
        }

        if (trimmed[^1] is '.' or ',' or ';' or ':' or '!' or '?')
        {
            return false;
        }

        var wordCount = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return wordCount <= MaxShortLineHeadingWords;
    }

    private static bool IsAllCapsHeading(string trimmed)
    {
        var letters = 0;
        foreach (var character in trimmed)
        {
            if (char.IsLower(character))
            {
                return false;
            }

            if (char.IsLetter(character))
            {
                letters++;
            }
        }

        return letters >= 2;
    }

    private IEnumerable<TextUnit> SplitSection(Section section)
    {
        var units = new List<TextUnit>();
        if (section.Heading is not null)
        {
            units.Add(new TextUnit(section.Heading, section.HeadingPage));
        }

        units.AddRange(section.Paragraphs);

        var fullText = string.Join("\n\n", units.Select(unit => unit.Text));
        if (fullText.Length == 0)
        {
            yield break;
        }

        if (fullText.Length <= _options.MaxChunkSize)
        {
            yield return new TextUnit(fullText, units[0].PageNumber);
            yield break;
        }

        // The section must be split; reserve room so the overlap prepended to
        // continuation pieces (plus its newline separator) never pushes a
        // chunk past the max size.
        var target = _options.OverlapSize > 0
            ? Math.Max(1, _options.MaxChunkSize - _options.OverlapSize - 1)
            : _options.MaxChunkSize;

        var pieces = PackUnits(SplitOversizedUnits(units, target), target);

        for (var i = 0; i < pieces.Count; i++)
        {
            if (i == 0 || _options.OverlapSize == 0)
            {
                yield return pieces[i];
                continue;
            }

            var overlap = TailOverlap(pieces[i - 1].Text, _options.OverlapSize);
            var text = overlap.Length > 0 ? overlap + "\n" + pieces[i].Text : pieces[i].Text;
            yield return pieces[i] with { Text = text };
        }
    }

    /// <summary>Splits any unit longer than <paramref name="target"/> at sentence, then word, boundaries.</summary>
    private static List<TextUnit> SplitOversizedUnits(List<TextUnit> units, int target)
    {
        var atoms = new List<TextUnit>();

        foreach (var unit in units)
        {
            if (unit.Text.Length <= target)
            {
                atoms.Add(unit);
                continue;
            }

            foreach (var sentence in SentenceBoundaryPattern.Split(unit.Text))
            {
                if (sentence.Length == 0)
                {
                    continue;
                }

                if (sentence.Length <= target)
                {
                    atoms.Add(new TextUnit(sentence, unit.PageNumber));
                    continue;
                }

                foreach (var fragment in HardSplit(sentence, target))
                {
                    atoms.Add(new TextUnit(fragment, unit.PageNumber));
                }
            }
        }

        return atoms;
    }

    /// <summary>Greedily packs atoms into pieces no longer than <paramref name="target"/>.</summary>
    private static List<TextUnit> PackUnits(List<TextUnit> atoms, int target)
    {
        var pieces = new List<TextUnit>();
        var builder = new StringBuilder();
        int? piecePage = null;

        foreach (var atom in atoms)
        {
            if (builder.Length > 0 && builder.Length + 2 + atom.Text.Length > target)
            {
                pieces.Add(new TextUnit(builder.ToString(), piecePage));
                builder.Clear();
                piecePage = null;
            }

            if (builder.Length == 0)
            {
                piecePage = atom.PageNumber;
            }
            else
            {
                builder.Append("\n\n");
            }

            builder.Append(atom.Text);
        }

        if (builder.Length > 0)
        {
            pieces.Add(new TextUnit(builder.ToString(), piecePage));
        }

        return pieces;
    }

    /// <summary>Breaks text into fragments of at most <paramref name="target"/> characters, preferring whitespace.</summary>
    private static IEnumerable<string> HardSplit(string text, int target)
    {
        var start = 0;

        while (text.Length - start > target)
        {
            var window = text.AsSpan(start, target);
            var breakAt = window.LastIndexOfAny(' ', '\t', '\n');
            if (breakAt <= 0)
            {
                breakAt = target;
            }

            var fragment = text.Substring(start, breakAt).Trim();
            if (fragment.Length > 0)
            {
                yield return fragment;
            }

            start += breakAt;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }

        if (start < text.Length)
        {
            var fragment = text[start..].Trim();
            if (fragment.Length > 0)
            {
                yield return fragment;
            }
        }
    }

    /// <summary>Takes the trailing overlap of a chunk, snapped forward to a whitespace boundary.</summary>
    private static string TailOverlap(string text, int overlapSize)
    {
        if (overlapSize <= 0)
        {
            return string.Empty;
        }

        var take = Math.Min(overlapSize, text.Length);
        var window = text[^take..];

        if (take < text.Length)
        {
            // Avoid starting the overlap mid-word.
            var firstWhitespace = window.IndexOfAny([' ', '\t', '\n']);
            if (firstWhitespace >= 0 && firstWhitespace + 1 < window.Length)
            {
                window = window[(firstWhitespace + 1)..];
            }
        }

        return window.Trim();
    }
}
