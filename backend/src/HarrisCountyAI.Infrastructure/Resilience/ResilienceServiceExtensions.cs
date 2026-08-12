using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Resilience;

/// <summary>
/// Registers the shared Azure retry and timeout budget. Must be registered
/// before the Azure client factories that read it.
/// </summary>
public static class ResilienceServiceExtensions
{
    public static IServiceCollection AddAzureResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AzureResilienceOptions>()
            .Bind(configuration.GetSection(AzureResilienceOptions.SectionName))
            .Validate(
                options => options.MaxRetryAttempts is >= 0 and <= 10,
                $"{AzureResilienceOptions.SectionName}:{nameof(AzureResilienceOptions.MaxRetryAttempts)} must be between 0 and 10.")
            .Validate(
                options => options.RetryBaseDelayMilliseconds > 0,
                $"{AzureResilienceOptions.SectionName}:{nameof(AzureResilienceOptions.RetryBaseDelayMilliseconds)} must be greater than zero.")
            .Validate(
                options => options.MaxRetryDelaySeconds > 0,
                $"{AzureResilienceOptions.SectionName}:{nameof(AzureResilienceOptions.MaxRetryDelaySeconds)} must be greater than zero.")
            .Validate(
                options => options.NetworkTimeoutSeconds > 0,
                $"{AzureResilienceOptions.SectionName}:{nameof(AzureResilienceOptions.NetworkTimeoutSeconds)} must be greater than zero.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Resolves the resilience budget, falling back to the defaults when the
    /// options were never registered — so a service registration that composes
    /// standalone (as several of the search and embedding extensions do) still
    /// gets a sane retry policy instead of the SDK's unconfigured one.
    /// </summary>
    internal static AzureResilienceOptions GetResilienceOptions(this IServiceProvider provider)
        => provider.GetService<IOptions<AzureResilienceOptions>>()?.Value ?? new AzureResilienceOptions();
}
