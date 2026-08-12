using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarrisCountyAI.Application.Evaluation.Judging;

public static class AnswerJudgeServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IAnswerJudge"/>. Requires <c>ILanguageModelService</c>
    /// (wired by <c>AddLanguageModel</c> in Infrastructure). Try-add semantics, so
    /// evaluation harnesses and the composition root can both call it safely.
    /// </summary>
    /// <remarks>
    /// Not called from <c>AddInfrastructure</c> on purpose. The judge is a
    /// development-time evaluator, and registering it in the application's
    /// composition root would invite it into a request path where it would add a
    /// second model call — and a second thing to be wrong — to every answer.
    /// Harnesses opt in explicitly.
    /// </remarks>
    public static IServiceCollection AddAnswerJudge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IAnswerJudge, AnswerJudge>();
        return services;
    }
}
