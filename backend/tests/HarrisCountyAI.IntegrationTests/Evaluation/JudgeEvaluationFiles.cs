namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>Locations of the committed judge evaluation files, relative to the evaluation root.</summary>
public static class JudgeEvaluationFiles
{
    /// <summary>Human labels the judge is measured against.</summary>
    public static readonly string[] ManualReviews = ["datasets", "generation", "manual-review.json"];

    /// <summary>The committed offline baseline, produced from the scripted judge.</summary>
    public static readonly string[] FixtureBaseline =
        ["datasets", "generation", "results", "judge-baseline-fixture.json"];

    /// <summary>Where a live judge run writes its report.</summary>
    public static readonly string[] LiveResult =
        ["datasets", "generation", "results", "judge-latest-live.json"];
}
