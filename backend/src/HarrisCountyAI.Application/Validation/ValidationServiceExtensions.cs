using HarrisCountyAI.Application.Validation.Comparison;
using HarrisCountyAI.Application.Validation.GetValidationReport;
using HarrisCountyAI.Application.Validation.RunValidation;
using HarrisCountyAI.Application.Validation.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.Application.Validation;

/// <summary>Service registration for the validation services, workflows, and use-case handlers.</summary>
public static class ValidationServiceExtensions
{
    public static IServiceCollection AddValidation(this IServiceCollection services)
    {
        services.AddScoped<DocumentValidationService>();
        // Scoped, not singleton: the workflow may consume ISemanticValidationService,
        // which is scoped to the request.
        services.AddScoped<IWorkflowDefinition, FloodplainDevelopmentPermitWorkflow>();

        services.AddRequirementComparison();

        services.AddScoped<RunValidationHandler>();
        services.AddScoped<GetLatestValidationReportHandler>();
        services.AddScoped<GetValidationReportHandler>();

        return services;
    }
}
