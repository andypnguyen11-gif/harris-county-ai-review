using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Validates <see cref="EmbeddingOptions"/> when the options instance is first resolved.
/// </summary>
public sealed class EmbeddingOptionsValidator : IValidateOptions<EmbeddingOptions>
{
    public ValidateOptionsResult Validate(string? name, EmbeddingOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            failures.Add($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.Endpoint)} is required.");
        }
        else if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
        {
            failures.Add($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.Endpoint)} must be an absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.ApiKey)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Deployment))
        {
            failures.Add($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.Deployment)} is required.");
        }

        if (options.MaxBatchSize < 1)
        {
            failures.Add($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.MaxBatchSize)} must be at least 1.");
        }

        if (options.TimeoutSeconds < 1)
        {
            failures.Add($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.TimeoutSeconds)} must be at least 1.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
