namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>
/// Locates the repository's <c>evaluation/</c> tree from a test assembly and
/// reads and writes the files in it.
/// </summary>
/// <remarks>
/// Datasets and baselines are read from the working tree rather than copied
/// into the test output, because the harness also has to write regenerated
/// baselines back to the same files. Set <c>EVALUATION_ROOT</c> to override the
/// discovered location (useful when running from a packaged output directory).
/// </remarks>
public static class EvaluationWorkspace
{
    /// <summary>Environment variable that overrides directory discovery.</summary>
    public const string RootOverrideVariable = "EVALUATION_ROOT";

    /// <summary>Environment variable that, when set to 1/true, lets baseline tests rewrite their committed files.</summary>
    public const string UpdateBaselinesVariable = "UPDATE_EVALUATION_BASELINE";

    private static readonly Lazy<string> LazyRoot = new(Discover);

    /// <summary>Absolute path of the repository's <c>evaluation/</c> directory.</summary>
    public static string Root => LazyRoot.Value;

    /// <summary>True when the caller asked for committed baselines to be regenerated instead of asserted.</summary>
    public static bool ShouldUpdateBaselines =>
        IsEnabled(Environment.GetEnvironmentVariable(UpdateBaselinesVariable));

    /// <summary>Resolves a path relative to <see cref="Root"/>.</summary>
    public static string Path(params string[] segments) =>
        System.IO.Path.Combine([Root, .. segments]);

    /// <summary>Reads a UTF-8 text file relative to <see cref="Root"/>.</summary>
    public static string ReadText(params string[] segments) => File.ReadAllText(Path(segments));

    /// <summary>True when a file relative to <see cref="Root"/> exists.</summary>
    public static bool Exists(params string[] segments) => File.Exists(Path(segments));

    /// <summary>
    /// Writes a UTF-8 text file relative to <see cref="Root"/>, creating the
    /// directory and normalizing line endings so a baseline regenerated on
    /// Windows and on macOS produces the same bytes.
    /// </summary>
    public static void WriteText(string content, params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = Path(segments);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
    }

    /// <summary>True when the value reads as an explicit opt-in.</summary>
    public static bool IsEnabled(string? value) =>
        value is not null
        && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static string Discover()
    {
        var configured = Environment.GetEnvironmentVariable(RootOverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return System.IO.Path.GetFullPath(configured);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, "evaluation", "datasets");
            if (Directory.Exists(candidate))
            {
                return System.IO.Path.Combine(directory.FullName, "evaluation");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find an 'evaluation/datasets' directory above {AppContext.BaseDirectory}. "
            + $"Set {RootOverrideVariable} to the repository's evaluation directory.");
    }
}
