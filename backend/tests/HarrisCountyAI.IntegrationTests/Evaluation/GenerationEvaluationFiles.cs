namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>Locations of the committed generation evaluation files, relative to the evaluation root.</summary>
public static class GenerationEvaluationFiles
{
    /// <summary>The curated question set with expected outcomes, facts, and citation titles.</summary>
    public static readonly string[] Dataset = ["datasets", "generation", "questions.json"];

    /// <summary>The committed offline baseline, produced from the fixture corpus and the scripted model.</summary>
    public static readonly string[] FixtureBaseline =
        ["datasets", "generation", "results", "baseline-fixture.json"];

    /// <summary>Where a live run writes its report.</summary>
    public static readonly string[] LiveResult = ["datasets", "generation", "results", "latest-live.json"];
}
