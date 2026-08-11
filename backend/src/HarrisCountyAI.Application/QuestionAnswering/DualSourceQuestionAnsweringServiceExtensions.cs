using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Application.QuestionAnswering;

public static class DualSourceQuestionAnsweringServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IDualSourceQuestionAnsweringService"/>. Shares the
    /// same dependencies as the single-scope path — <c>IRetrievalService</c>
    /// (see <c>RetrievalServiceCollectionExtensions.AddCorpusRetrieval</c> in
    /// Infrastructure) and <c>ILanguageModelService</c> (wired by
    /// <c>AddLanguageModel</c>) — so it is called from
    /// <see cref="QuestionAnsweringServiceExtensions.AddQuestionAnswering"/>
    /// rather than needing its own composition-root line. Try-add semantics,
    /// so tests and the composition root can both call it safely.
    /// </summary>
    public static IServiceCollection AddDualSourceQuestionAnswering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IDualSourceQuestionAnsweringService, DualSourceQuestionAnsweringService>();
        return services;
    }
}
