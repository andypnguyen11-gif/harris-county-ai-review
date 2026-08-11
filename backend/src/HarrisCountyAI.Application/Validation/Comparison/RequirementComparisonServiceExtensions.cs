using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Application.Validation.Comparison;

public static class RequirementComparisonServiceExtensions
{
    /// <summary>
    /// Registers the requirement comparison engine and the requirement
    /// catalogs it compares against. Requires <c>IRetrievalService</c> (see
    /// <c>RetrievalServiceCollectionExtensions.AddCorpusRetrieval</c> in
    /// Infrastructure) and <c>ISemanticValidationService</c> (wired by
    /// <c>AddSemanticValidation</c>). Called from
    /// <see cref="ValidationServiceExtensions.AddValidation"/>, so it needs no
    /// composition-root line of its own. Try-add semantics for the service;
    /// catalogs are registered once per implementation type, so repeated calls
    /// do not duplicate them.
    /// </summary>
    public static IServiceCollection AddRequirementComparison(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRequirementCatalog, FloodplainDevelopmentPermitRequirements>());
        services.TryAddScoped<IRequirementComparisonService, RequirementComparisonService>();
        return services;
    }
}
