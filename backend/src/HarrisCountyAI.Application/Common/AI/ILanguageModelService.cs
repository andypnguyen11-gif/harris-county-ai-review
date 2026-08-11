namespace HarrisCountyAI.Application.Common.AI;

/// <summary>
/// Provider-agnostic abstraction over a hosted language model. The application layer
/// depends only on this interface; concrete deployments (e.g. Azure OpenAI) live in
/// Infrastructure so the model remains replaceable and testable.
/// </summary>
public interface ILanguageModelService
{
    /// <summary>Generates a single completion for the given request.</summary>
    /// <param name="request">The prompts and generation parameters.</param>
    /// <param name="cancellationToken">Cancels the in-flight request when signaled.</param>
    /// <returns>The model output together with usage and timing metadata.</returns>
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken);
}
