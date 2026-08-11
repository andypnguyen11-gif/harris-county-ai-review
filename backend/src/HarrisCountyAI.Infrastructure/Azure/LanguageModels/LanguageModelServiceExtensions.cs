using HarrisCountyAI.Application.Common.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>Dependency injection registration for the language model abstraction.</summary>
public static class LanguageModelServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ILanguageModelService"/> backed by Azure OpenAI, binding
    /// <see cref="LanguageModelOptions"/> from the <c>LanguageModel</c> configuration
    /// section and validating it eagerly at host startup.
    /// </summary>
    public static IServiceCollection AddLanguageModel(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LanguageModelOptions>()
            .Bind(configuration.GetSection(LanguageModelOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LanguageModelOptions>, LanguageModelOptionsValidator>());

        services.TryAddSingleton<ILanguageModelService, AzureLanguageModelService>();

        return services;
    }
}
