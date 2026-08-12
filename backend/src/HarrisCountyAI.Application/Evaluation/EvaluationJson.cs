using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarrisCountyAI.Application.Evaluation;

/// <summary>
/// Shared JSON settings for every evaluation dataset and result file, so a
/// committed baseline is byte-stable across machines and a regenerated file
/// produces a reviewable diff rather than a reformat.
/// </summary>
public static class EvaluationJson
{
    /// <summary>Options for reading committed datasets: camelCase, comment- and trailing-comma-tolerant.</summary>
    public static JsonSerializerOptions ReadOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Options for writing result files: camelCase, indented, nulls kept so the shape stays explicit.</summary>
    public static JsonSerializerOptions WriteOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes <paramref name="value"/> with <see cref="WriteOptions"/>, newline-terminated.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WriteOptions) + Environment.NewLine;
}
