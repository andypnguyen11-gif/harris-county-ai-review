using Azure;
using Azure.AI.DocumentIntelligence;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// Service registration for the document extraction pipeline backed by Azure
/// AI Document Intelligence.
/// </summary>
public static class DocumentIntelligenceServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IDocumentExtractionService"/> backed by Azure AI
    /// Document Intelligence, the <see cref="IDocumentProcessingService"/>
    /// orchestrator, and their dependencies.
    /// <see cref="DocumentIntelligenceOptions"/> is bound from the
    /// <c>DocumentIntelligence</c> configuration section and validated at
    /// startup.
    /// </summary>
    public static IServiceCollection AddDocumentIntelligence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DocumentIntelligenceOptions>()
            .Bind(configuration.GetSection(DocumentIntelligenceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DocumentIntelligenceOptions>, DocumentIntelligenceOptionsValidator>();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<DocumentIntelligenceOptions>>().Value;
            return new DocumentIntelligenceClient(new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey));
        });

        services.AddSingleton<IDocumentIntelligenceAnalyzeClient, DocumentIntelligenceAnalyzeClient>();
        services.AddSingleton<AnalyzeResultMapper>();
        services.AddSingleton<IDocumentExtractionService, AzureDocumentExtractionService>();

        services.AddScoped<INormalizedDocumentRepository, NormalizedDocumentRepository>();
        services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();

        return services;
    }
}
