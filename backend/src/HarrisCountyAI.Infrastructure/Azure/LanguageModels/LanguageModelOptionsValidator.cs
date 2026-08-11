using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Validates <see cref="LanguageModelOptions"/> so misconfiguration fails fast at
/// startup with actionable messages instead of failing on the first model call.
/// </summary>
public sealed class LanguageModelOptionsValidator : IValidateOptions<LanguageModelOptions>
{
    public ValidateOptionsResult Validate(string? name, LanguageModelOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            failures.Add(
                "LanguageModel:Endpoint is required. Set it to the Azure OpenAI resource endpoint, " +
                "e.g. https://<resource>.openai.azure.com/.");
        }
        else if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"LanguageModel:Endpoint '{options.Endpoint}' is not a valid absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add(
                "LanguageModel:ApiKey is required. Provide it via configuration or the " +
                "LanguageModel__ApiKey environment variable; never commit it to source control.");
        }

        if (string.IsNullOrWhiteSpace(options.Deployment))
        {
            failures.Add("LanguageModel:Deployment is required. Set it to the Azure OpenAI deployment name.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add($"LanguageModel:TimeoutSeconds must be greater than zero (was {options.TimeoutSeconds}).");
        }

        if (options.MaxOutputTokens <= 0)
        {
            failures.Add($"LanguageModel:MaxOutputTokens must be greater than zero (was {options.MaxOutputTokens}).");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
