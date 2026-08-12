namespace HarrisCountyAI.Application.Evaluation;

/// <summary>
/// How the evidence behind an evaluation report was produced. Every committed
/// result file records this so a reader never mistakes a deterministic offline
/// measurement for a measurement of the deployed Azure stack.
/// </summary>
public enum EvaluationRunType
{
    /// <summary>
    /// The run used a deterministic offline fixture (a committed stub corpus and,
    /// for generation, a scripted model). Reproducible on any machine with no
    /// Azure account, but it measures the harness and the committed dataset —
    /// not production retrieval or a production model.
    /// </summary>
    Fixture,

    /// <summary>
    /// The run called the live Azure services configured in the environment.
    /// These numbers describe the real system at a point in time and cost money
    /// to reproduce.
    /// </summary>
    Live,
}
