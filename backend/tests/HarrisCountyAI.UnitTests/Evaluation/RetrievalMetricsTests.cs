using HarrisCountyAI.Application.Evaluation.Retrieval;

namespace HarrisCountyAI.UnitTests.Evaluation;

/// <summary>
/// Recall@K and MRR are the numbers the project will argue about, so their
/// definitions are pinned against hand-computed examples.
/// </summary>
public sealed class RetrievalMetricsTests
{
    private static readonly IReadOnlyList<int> Cutoffs = [1, 3, 5];

    [Fact]
    public void Recall_Counts_Questions_Whose_Evidence_Landed_Inside_The_Cutoff()
    {
        // Ranks 1, 2, 4, and a miss: Recall@1 = 1/4, Recall@3 = 2/4, Recall@5 = 3/4.
        var metrics = RetrievalMetrics.FromRanks([1, 2, 4, null], Cutoffs);

        Assert.Equal(4, metrics.QuestionCount);
        Assert.Equal(3, metrics.HitCount);
        Assert.Equal(0.25, metrics.RecallAt1);
        Assert.Equal(0.5, metrics.RecallAt3);
        Assert.Equal(0.75, metrics.RecallAt5);
    }

    [Fact]
    public void Mean_Reciprocal_Rank_Rewards_Ranking_The_Evidence_First()
    {
        // (1/1 + 1/2 + 1/4 + 0) / 4 = 0.4375.
        var metrics = RetrievalMetrics.FromRanks([1, 2, 4, null], Cutoffs);

        Assert.Equal(0.4375, metrics.MeanReciprocalRank);
    }

    [Fact]
    public void Two_Runs_With_Equal_Recall_Are_Separated_By_Mean_Reciprocal_Rank()
    {
        var alwaysFirst = RetrievalMetrics.FromRanks([1, 1, 1], Cutoffs);
        var alwaysThird = RetrievalMetrics.FromRanks([3, 3, 3], Cutoffs);

        Assert.Equal(alwaysFirst.RecallAt5, alwaysThird.RecallAt5);
        Assert.True(alwaysFirst.MeanReciprocalRank > alwaysThird.MeanReciprocalRank);
    }

    [Fact]
    public void A_Run_That_Retrieved_Nothing_Scores_Zero_Rather_Than_Failing()
    {
        var metrics = RetrievalMetrics.FromRanks([null, null], Cutoffs);

        Assert.Equal(0d, metrics.RecallAt1);
        Assert.Equal(0d, metrics.RecallAt5);
        Assert.Equal(0d, metrics.MeanReciprocalRank);
        Assert.Equal(0, metrics.HitCount);
    }

    [Fact]
    public void An_Empty_Question_Set_Yields_Zeroes_Instead_Of_Dividing_By_Zero()
    {
        var metrics = RetrievalMetrics.FromRanks([], Cutoffs);

        Assert.Equal(0, metrics.QuestionCount);
        Assert.Equal(0d, metrics.RecallAt1);
        Assert.Equal(0d, metrics.MeanReciprocalRank);
    }

    [Fact]
    public void Values_Are_Rounded_So_Committed_Baselines_Stay_Diff_Stable()
    {
        // 1/3 would otherwise serialize with full double precision.
        var metrics = RetrievalMetrics.FromRanks([1, null, null], Cutoffs);

        Assert.Equal(0.3333, metrics.RecallAt1);
        Assert.Equal(0.3333, metrics.MeanReciprocalRank);
    }

    [Fact]
    public void Only_Requested_Cutoffs_Are_Reported()
    {
        var metrics = RetrievalMetrics.FromRanks([1, 2], [2]);

        Assert.Null(metrics.RecallAt1);
        Assert.Null(metrics.RecallAt5);
        Assert.Equal(1d, metrics.RecallAtK[2]);
    }

    [Fact]
    public void Duplicate_Cutoffs_Collapse_And_Are_Reported_In_Ascending_Order()
    {
        var metrics = RetrievalMetrics.FromRanks([1], [5, 1, 5]);

        Assert.Equal([1, 5], metrics.RecallAtK.Keys.Order());
    }
}
