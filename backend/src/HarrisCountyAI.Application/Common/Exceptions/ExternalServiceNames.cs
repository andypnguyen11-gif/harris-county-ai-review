namespace HarrisCountyAI.Application.Common.Exceptions;

/// <summary>
/// Display names for the external dependencies the application talks to.
/// These are the only dependency identifiers that may reach a client: they
/// name a capability ("Search"), never an endpoint, resource, region, or
/// credential. Everything an operator needs to find the actual resource is in
/// the logs, reachable through the correlation id on the error response.
/// </summary>
public static class ExternalServiceNames
{
    /// <summary>Azure AI Search — retrieval and indexing.</summary>
    public const string Search = "Search";

    /// <summary>Azure AI Document Intelligence — document extraction.</summary>
    public const string DocumentIntelligence = "Document extraction";

    /// <summary>The chat-completions model deployment.</summary>
    public const string LanguageModel = "Language model";

    /// <summary>The embeddings model deployment.</summary>
    public const string Embeddings = "Embeddings";

    /// <summary>Azure Blob Storage — uploaded files and corpus documents.</summary>
    public const string DocumentStorage = "Document storage";
}
