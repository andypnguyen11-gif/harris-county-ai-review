using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Application.QuestionAnswering;

public static class QuestionAnsweringServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IQuestionAnsweringService"/>. Requires
    /// <c>IRetrievalService</c> (see
    /// <c>RetrievalServiceCollectionExtensions.AddCorpusRetrieval</c> in
    /// Infrastructure) and <c>ILanguageModelService</c> (already wired by
    /// <c>AddLanguageModel</c>). Try-add semantics, so tests and the
    /// composition root can both call it safely.
    /// </summary>
    public static IServiceCollection AddQuestionAnswering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IQuestionAnsweringService, QuestionAnsweringService>();
        return services;
    }
}
