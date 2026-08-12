namespace HarrisCountyAI.UnitTests.Security;

/// <summary>
/// The adversarial permit documents under <c>Security/TestData</c>. Each file is a
/// plausible submission carrying one class of injection payload, kept on disk rather
/// than inline so the payloads read the way a real document would and so new attacks
/// can be added without touching test code.
/// </summary>
internal static class InjectionDocuments
{
    /// <summary>A document that tells the model to disregard its instructions outright.</summary>
    public const string DirectInstructionOverride = "direct-instruction-override.txt";

    /// <summary>A document that closes a real evidence block and opens a forged system block.</summary>
    public const string ForgedDelimiter = "forged-delimiter.txt";

    /// <summary>A document that invents delimiter names no code emits — what a blacklist of known tokens would miss.</summary>
    public const string InventedDelimiter = "invented-delimiter.txt";

    /// <summary>An applicant document asserting county policy, to be mistaken for a requirement.</summary>
    public const string ForgedCountyRequirement = "forged-county-requirement.txt";

    /// <summary>A document dictating the JSON verdict the semantic validator must return.</summary>
    public const string VerdictCoercion = "verdict-coercion.txt";

    /// <summary>A document demanding the system prompt and other cases' evidence back in the answer.</summary>
    public const string ExfiltrationRequest = "exfiltration-request.txt";

    /// <summary>A document hiding instructions in Unicode tag characters and zero-width padding.</summary>
    public const string HiddenUnicodeInstruction = "hidden-unicode-instruction.txt";

    /// <summary>Every injection document, for theories that must hold across all of them.</summary>
    public static TheoryData<string> All =>
    [
        DirectInstructionOverride,
        ForgedDelimiter,
        InventedDelimiter,
        ForgedCountyRequirement,
        VerdictCoercion,
        ExfiltrationRequest,
        HiddenUnicodeInstruction,
    ];

    /// <summary>Reads one injection document from the test output directory.</summary>
    public static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Security", "TestData", fileName);
        Assert.True(File.Exists(path), $"Injection test document not found: {path}");
        return File.ReadAllText(path);
    }
}
