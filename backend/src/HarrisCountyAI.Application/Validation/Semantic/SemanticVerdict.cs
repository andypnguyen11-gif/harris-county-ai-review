namespace HarrisCountyAI.Application.Validation.Semantic;

/// <summary>The model's judgment of whether document content satisfies a requirement.</summary>
public enum SemanticVerdict
{
    /// <summary>The content clearly satisfies the requirement.</summary>
    Pass,

    /// <summary>The content clearly does not satisfy the requirement.</summary>
    Fail,

    /// <summary>The content is ambiguous or the model is not confident; a reviewer must decide.</summary>
    NeedsHumanReview,

    /// <summary>The evaluation could not run to a conclusion (model error or uninterpretable response). Fail-closed default.</summary>
    UnableToDetermine,
}
