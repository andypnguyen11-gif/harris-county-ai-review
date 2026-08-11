using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Application.Validation.Semantic;

public static class SemanticValidationServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ISemanticValidationService"/>. Requires an
    /// <c>ILanguageModelService</c> registration (see
    /// <c>LanguageModelServiceExtensions.AddLanguageModel</c> in Infrastructure); like the
    /// language model itself, this is intentionally not wired into <c>Program.cs</c> yet —
    /// both are wired together when the first API consumer (the validation report endpoint)
    /// lands.
    /// </summary>
    public static IServiceCollection AddSemanticValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ISemanticValidationService, SemanticValidationService>();
        return services;
    }
}
