using HarrisCountyAI.Application.Documents.GetDocument;
using HarrisCountyAI.Application.Documents.GetDocumentContent;
using HarrisCountyAI.Application.Documents.GetDocuments;
using HarrisCountyAI.Application.Documents.Indexing;
using HarrisCountyAI.Application.Documents.ProcessDocument;
using HarrisCountyAI.Application.Documents.UploadDocument;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.Application.Documents;

/// <summary>Service registration for the document use-case handlers.</summary>
public static class DocumentHandlerServiceExtensions
{
    public static IServiceCollection AddDocumentHandlers(this IServiceCollection services)
    {
        services.AddScoped<UploadDocumentHandler>();
        services.AddScoped<GetDocumentsHandler>();
        services.AddScoped<GetDocumentHandler>();
        services.AddScoped<GetDocumentContentHandler>();
        services.AddScoped<ProcessDocumentHandler>();
        services.AddCaseDocumentIndexing();

        return services;
    }
}
