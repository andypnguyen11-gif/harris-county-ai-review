using HarrisCountyAI.Application.Validation.Semantic;

namespace HarrisCountyAI.UnitTests.Validation.Comparison;

/// <summary>
/// In-memory <see cref="ISemanticValidationService"/> that records every
/// request and returns a scripted verdict. Its call count is what the
/// deterministic-first tests assert on: a requirement that code can settle
/// must leave this fake untouched.
/// </summary>
public sealed class FakeSemanticValidationService : ISemanticValidationService
{
    public List<SemanticValidationRequest> Requests { get; } = [];

    /// <summary>Number of times a semantic evaluation was requested.</summary>
    public int CallCount => Requests.Count;

    /// <summary>Verdict returned for every evaluation.</summary>
    public SemanticVerdict Verdict { get; set; } = SemanticVerdict.Pass;

    /// <summary>Reasoning returned for every evaluation.</summary>
    public string Reasoning { get; set; } = "The submitted content satisfies the requirement.";

    public string PromptVersion { get; set; } = "semantic-validation/v2";

    public string ModelDeployment { get; set; } = "fake-deployment";

    public Task<SemanticValidationResult> EvaluateAsync(
        SemanticValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);

        return Task.FromResult(new SemanticValidationResult
        {
            Verdict = Verdict,
            Reasoning = Reasoning,
            PromptVersion = PromptVersion,
            ModelDeployment = ModelDeployment,
        });
    }
}
