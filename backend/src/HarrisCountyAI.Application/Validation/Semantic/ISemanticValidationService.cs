namespace HarrisCountyAI.Application.Validation.Semantic;

/// <summary>
/// Evaluates whether document content satisfies a requirement using language model reasoning.
/// Reserved for checks that need semantic understanding — deterministic checks (missing fields,
/// signatures, dates) never go through this service.
/// </summary>
public interface ISemanticValidationService
{
    /// <summary>
    /// Runs one semantic evaluation. Fails closed: any model error or uninterpretable response
    /// yields <see cref="SemanticVerdict.UnableToDetermine"/> rather than an exception. Only
    /// caller cancellation propagates.
    /// </summary>
    Task<SemanticValidationResult> EvaluateAsync(SemanticValidationRequest request, CancellationToken cancellationToken = default);
}
