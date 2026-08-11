namespace HarrisCountyAI.Api.Contracts.Retrieval;

/// <summary>Request body for the temporary retrieval debug endpoint.</summary>
public sealed record RetrievalDebugRequest
{
    /// <summary>Natural-language query to retrieve corpus passages for.</summary>
    public string? Query { get; init; }

    /// <summary>Number of chunks to retrieve; defaults to the service default when null.</summary>
    public int? TopK { get; init; }

    /// <summary>Optional department filter.</summary>
    public string? Department { get; init; }

    /// <summary>Optional permit type filter.</summary>
    public string? PermitType { get; init; }

    /// <summary>Optional document category filter.</summary>
    public string? DocumentType { get; init; }
}
