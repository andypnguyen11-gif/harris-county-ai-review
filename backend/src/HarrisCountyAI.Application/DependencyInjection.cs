using HarrisCountyAI.Application.Cases.CreateCase;
using HarrisCountyAI.Application.Cases.GetCase;
using HarrisCountyAI.Application.Cases.GetCases;
using HarrisCountyAI.Application.Cases.UpdateCase;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Application.Validation;
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

        services.AddSingleton<IDocumentNormalizationService, DocumentNormalizationService>();

        return services;
    }
}
