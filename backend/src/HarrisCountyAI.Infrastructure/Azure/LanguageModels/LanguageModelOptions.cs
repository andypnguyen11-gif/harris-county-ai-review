namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Configuration for the Azure-hosted language model, bound from the
/// <c>LanguageModel</c> configuration section. The API key must come from
/// configuration or environment (e.g. <c>LanguageModel__ApiKey</c>); it is
/// never hard-coded.
/// </summary>
public sealed class LanguageModelOptions
{
    /// <summary>Name of the configuration section this type binds to.</summary>
    public const string SectionName = "LanguageModel";

    /// <summary>Azure OpenAI resource endpoint, e.g. <c>https://my-resource.openai.azure.com/</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>API key for the Azure OpenAI resource. Supply via configuration or environment only.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Name of the model deployment to call. This is the name the deployment was
    /// given in the Azure OpenAI resource, which is not the name of the model
    /// behind it: a deployment called <c>chat</c> may serve <c>gpt-5-mini</c>,
    /// and the resource is addressed only by the former. Naming the model here
    /// is answered with a 404 on every request.
    /// </summary>
    public string Deployment { get; set; } = string.Empty;

    /// <summary>
    /// Whether the deployment accepts a temperature. Reasoning models — the
    /// o-series and GPT-5 — support only their own default and reject any
    /// request that names another, so against those this is false and the
    /// temperature is left off the request entirely.
    /// </summary>
    public bool SupportsTemperature { get; set; } = true;

    /// <summary>Per-request timeout in seconds. Defaults to 60.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Default maximum output tokens when a request does not specify its own limit.</summary>
    public int MaxOutputTokens { get; set; } = 1024;
}
