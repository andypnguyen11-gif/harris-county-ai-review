namespace HarrisCountyAI.IntegrationTests.Evaluation;

/// <summary>Locations of the committed retrieval evaluation files, relative to the evaluation root.</summary>
public static class RetrievalEvaluationFiles
{
    /// <summary>The curated question set.</summary>
    public static readonly string[] Dataset = ["datasets", "retrieval", "floodplain-questions.json"];

    /// <summary>The committed offline baseline, produced from the fixture corpus.</summary>
    public static readonly string[] FixtureBaseline =
        ["datasets", "retrieval", "results", "baseline-fixture.json"];

    /// <summary>Where a live run writes its report.</summary>
    public static readonly string[] LiveResult = ["datasets", "retrieval", "results", "latest-live.json"];
}
