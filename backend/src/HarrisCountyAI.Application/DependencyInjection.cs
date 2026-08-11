using HarrisCountyAI.Application.Cases.CreateCase;
using HarrisCountyAI.Application.Cases.GetCase;
using HarrisCountyAI.Application.Cases.GetCases;
using HarrisCountyAI.Application.Cases.UpdateCase;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Application.KnowledgeBase.DeactivateKnowledgeDocument;
using HarrisCountyAI.Application.KnowledgeBase.GetKnowledgeDocuments;
using HarrisCountyAI.Application.KnowledgeBase.Ingestion;
using HarrisCountyAI.Application.KnowledgeBase.UploadKnowledgeDocument;
using HarrisCountyAI.Application.Search.Chunking;
using HarrisCountyAI.Application.Validation;
using HarrisCountyAI.Application.Validation.Semantic;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateCaseHandler>();
        services.AddScoped<GetCaseHandler>();
        services.AddScoped<GetCasesHandler>();
        services.AddScoped<UpdateCaseHandler>();
        services.AddDocumentHandlers();
        services.AddValidation();
        services.AddSemanticValidation();

        services.AddScoped<UploadKnowledgeDocumentHandler>();
        services.AddScoped<GetKnowledgeDocumentsHandler>();
        services.AddScoped<DeactivateKnowledgeDocumentHandler>();
        services.AddScoped<IKnowledgeDocumentIngestionService, KnowledgeDocumentIngestionService>();
        services.AddSingleton<IDocumentChunkingService, StructureAwareChunkingService>();

        services.AddSingleton<IDocumentNormalizationService, DocumentNormalizationService>();

        return services;
    }
}
