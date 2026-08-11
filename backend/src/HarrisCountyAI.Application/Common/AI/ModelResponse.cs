namespace HarrisCountyAI.Application.Common.AI;

/// <summary>The result of a single language model generation.</summary>
public sealed record ModelResponse
{
    /// <summary>The generated text (or JSON, when structured output was requested).</summary>
    public required string Content { get; init; }

    /// <summary>Why generation stopped (e.g. "Stop", "Length", "ContentFilter").</summary>
    public required string FinishReason { get; init; }

    /// <summary>Token usage reported by the provider for this generation.</summary>
    public required ModelUsage Usage { get; init; }

    /// <summary>The deployment (model) that served the request.</summary>
    public required string ModelDeployment { get; init; }

    /// <summary>Wall-clock time the generation took.</summary>
    public required TimeSpan Elapsed { get; init; }
}

/// <summary>Token counts reported by the provider for a single generation.</summary>
public sealed record ModelUsage(int InputTokens, int OutputTokens)
{
    /// <summary>Combined input and output token count.</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>Usage placeholder for providers or fakes that report no counts.</summary>
    public static ModelUsage Empty { get; } = new(0, 0);
}
