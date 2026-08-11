using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

/// <summary>Validates <see cref="DocumentIntelligenceOptions"/> at startup.</summary>
public sealed class DocumentIntelligenceOptionsValidator : IValidateOptions<DocumentIntelligenceOptions>
{
    public ValidateOptionsResult Validate(string? name, DocumentIntelligenceOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            failures.Add($"{DocumentIntelligenceOptions.SectionName}:{nameof(options.Endpoint)} is required.");
        }
        else if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{DocumentIntelligenceOptions.SectionName}:{nameof(options.Endpoint)} must be an absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{DocumentIntelligenceOptions.SectionName}:{nameof(options.ApiKey)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            failures.Add($"{DocumentIntelligenceOptions.SectionName}:{nameof(options.ModelId)} is required.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add($"{DocumentIntelligenceOptions.SectionName}:{nameof(options.TimeoutSeconds)} must be greater than zero.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
