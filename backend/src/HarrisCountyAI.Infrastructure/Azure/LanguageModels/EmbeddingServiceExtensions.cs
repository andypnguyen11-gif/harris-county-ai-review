using HarrisCountyAI.Application.Search.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.LanguageModels;

/// <summary>
/// Registers the Azure OpenAI embedding service. Not wired into the application host yet;
/// composition happens when the ingestion pipeline consumes <see cref="IEmbeddingService"/>.
/// </summary>
public static class EmbeddingServiceExtensions
{
    public static IServiceCollection AddEmbeddingService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EmbeddingOptions>, EmbeddingOptionsValidator>());

        services.TryAddSingleton<IEmbeddingBatchClient, AzureOpenAIEmbeddingBatchClient>();
        services.TryAddSingleton<IEmbeddingService, AzureEmbeddingService>();

        return services;
    }
}
