using System.Text;
using System.Text.RegularExpressions;

namespace HarrisCountyAI.Application.Common.Security;

/// <summary>
/// The single sanitization boundary for text the system did not author — reviewer
/// questions, applicant-submitted document content, and passages retrieved from the
/// reference corpus. Every prompt builder routes untrusted text through
/// <see cref="Sanitize"/> before it is placed inside a delimited evidence block.
///
/// The defense is structural rather than a form of words. Prompts fence evidence
/// between tokens of the shape <c>&lt;&lt;&lt;NAME&gt;&gt;&gt;</c>, and the system
/// instruction — delivered on a separate channel, never concatenated into the user
/// prompt — tells the model that anything inside a fence is data. That framing only
/// holds if evidence cannot produce a token the model might read as a fence, so
/// sanitization enforces one invariant:
///
/// <b>Sanitized text contains no fence syntax at all</b> — no run of three or more
/// <c>&lt;</c> or <c>&gt;</c> characters survives, and no invisible character survives
/// that could reconstitute one or hide instructions from the humans reviewing the text.
///
/// Neutralizing the fence <i>shape</i> rather than a list of known delimiter literals is
/// deliberate. A blacklist stops only the delimiters that exist today: text that invents
/// a plausible new one (<c>&lt;&lt;&lt;SYSTEM&gt;&gt;&gt;</c>,
/// <c>&lt;&lt;&lt;END_OF_UNTRUSTED_DATA&gt;&gt;&gt;</c>) reads as a boundary to a model
/// even though no code emits it. Because nothing in this grammar can survive
/// sanitization, forging a boundary — known, renamed, or invented — is not possible,
/// and the fences in the finished prompt are exactly the ones the builder wrote.
///
/// Sanitization is lossy by design: legitimate text that happens to contain
/// <c>&gt;&gt;&gt;</c> (a quoted email, an ASCII arrow) is rewritten. County permit
/// material carries no meaning in those characters, and preserving them would mean
/// deciding case by case which ones are safe — the kind of judgment call this boundary
/// exists to avoid.
/// </summary>
public static partial class UntrustedText
{
    /// <summary>Replaces text that could be read as a section boundary. Named in the system prompts so the model knows the marker signals tampering, not an instruction.</summary>
    public const string NeutralizedDelimiterMarker = "[delimiter removed]";

    /// <summary>
    /// Builds a fence token in the canonical delimiter grammar. Prompt classes declare
    /// their delimiters as constants (they appear in system prompt literals), but this
    /// documents the one shape <see cref="Sanitize"/> is built to neutralize.
    /// </summary>
    public static string Fence(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"<<<{name}>>>";
    }

    /// <summary>
    /// Makes untrusted text safe to place inside a delimited evidence block: strips
    /// invisible and control characters, then replaces every fence-shaped token — and
    /// any leftover run of three or more angle brackets — with
    /// <see cref="NeutralizedDelimiterMarker"/>.
    /// </summary>
    /// <returns>Text guaranteed to satisfy <c>!<see cref="ContainsDelimiterSyntax"/>(result)</c>.</returns>
    public static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Invisibles come out first: a fence padded with zero-width characters
        // (<<<​SOURCES_END>>>) must collapse into the plain shape before the
        // fence rules run, or it would slip past them and still read as a boundary.
        var stripped = StripInvisibleCharacters(text);

        // Whole fence-shaped tokens first, so a forged delimiter is replaced by one
        // marker rather than leaving its name stranded between two of them.
        var neutralized = FenceTokenPattern().Replace(stripped, NeutralizedDelimiterMarker);

        // Then any remaining bracket run, which closes off partial and nested forms
        // (a lone ">>>", or "<<<<<<NAME>>>") and is what makes the no-fence-syntax
        // invariant hold unconditionally.
        return BracketRunPattern().Replace(neutralized, NeutralizedDelimiterMarker);
    }

    /// <summary>
    /// Whether the text contains anything a model could read as a section boundary.
    /// Always <see langword="false"/> for the output of <see cref="Sanitize"/>; used by
    /// callers and tests to assert that invariant directly.
    /// </summary>
    public static bool ContainsDelimiterSyntax(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains("<<<", StringComparison.Ordinal)
            || text.Contains(">>>", StringComparison.Ordinal);
    }

    /// <summary>
    /// Removes characters that carry no meaning in permit text but undermine the
    /// boundary: control characters (other than tab, carriage return, and newline),
    /// zero-width characters, bidirectional overrides that make rendered text differ
    /// from what the model reads, and the Unicode tag block, whose sole modern use is
    /// smuggling instructions that are invisible to a reviewer reading the document.
    /// </summary>
    private static string StripInvisibleCharacters(string text)
    {
        var builder = new StringBuilder(text.Length);
        var removedAny = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsDisallowed(rune.Value))
            {
                removedAny = true;
                continue;
            }

            builder.Append(rune);
        }

        return removedAny ? builder.ToString() : text;
    }

    private static bool IsDisallowed(int codePoint) => codePoint switch
    {
        '\t' or '\n' or '\r' => false,
        < 0x20 or 0x7F => true,                     // C0 controls and DEL
        >= 0x80 and <= 0x9F => true,                // C1 controls
        0x200B or 0x200C or 0x200D => true,         // zero-width space, non-joiner, joiner
        0x200E or 0x200F => true,                   // left-to-right / right-to-left marks
        >= 0x202A and <= 0x202E => true,            // bidirectional embedding and override
        >= 0x2060 and <= 0x2064 => true,            // word joiner and invisible operators
        >= 0x2066 and <= 0x2069 => true,            // bidirectional isolates
        0xFEFF => true,                             // byte order mark / zero-width no-break space
        >= 0xE0000 and <= 0xE007F => true,          // Unicode tag block
        _ => false,
    };

    /// <summary>
    /// A complete fence-shaped token: three or more opening brackets, a short run of
    /// non-bracket characters, three or more closing brackets. The length cap keeps the
    /// pattern from swallowing a paragraph that merely opens and closes with brackets.
    /// </summary>
    [GeneratedRegex(@"<{3,}[^<>]{0,200}>{3,}", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex FenceTokenPattern();

    /// <summary>Any leftover run of three or more angle brackets in either direction.</summary>
    [GeneratedRegex(@"<{3,}|>{3,}", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex BracketRunPattern();
}
