using HarrisCountyAI.Application.Evaluation.Retrieval;
using HarrisCountyAI.Application.QuestionAnswering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// Runs the generation evaluation dataset through the real question-answering
/// pipeline and scores what comes back.
/// </summary>
/// <remarks>
/// Deliberately the whole pipeline, not a prompt harness: the thing worth
/// measuring is what a reviewer would actually see, including retrieval, the
/// grounded prompt, citation resolution, and the fail-closed downgrade that
/// turns an uncitable answer into an insufficient-evidence response.
///
/// Everything scored here is deterministic — outcome agreement, citation
/// presence, expected-fact coverage by phrase match, and a lexical
/// unsupported-claim screen. No model judges the answer at this layer; that is
/// a separate, opt-in step, so these numbers stay free and reproducible.
/// </remarks>
public sealed class GenerationEvaluationRunner
{
    private readonly IQuestionAnsweringService _questionAnswering;
    private readonly IEvaluationEvidenceRecorder? _evidenceRecorder;
    private readonly ILogger<GenerationEvaluationRunner> _logger;

    /// <param name="questionAnswering">The pipeline under evaluation.</param>
    /// <param name="evidenceRecorder">
    /// Captures the passages each answer was grounded in. Optional: without it
    /// the run still scores outcomes, citations, and fact coverage, and reports
    /// unsupported-claim metrics as null rather than guessing.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public GenerationEvaluationRunner(
        IQuestionAnsweringService questionAnswering,
        IEvaluationEvidenceRecorder? evidenceRecorder = null,
        ILogger<GenerationEvaluationRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(questionAnswering);

        _questionAnswering = questionAnswering;
        _evidenceRecorder = evidenceRecorder;
        _logger = logger ?? NullLogger<GenerationEvaluationRunner>.Instance;
    }

    /// <summary>Answers and scores every question in <paramref name="dataset"/>.</summary>
    public async Task<GenerationEvaluationReport> RunAsync(
        GenerationEvaluationDataset dataset,
        GenerationEvaluationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        dataset.Validate();

        var runOptions = options ?? new GenerationEvaluationOptions();
        runOptions.Validate();

        var cases = new List<GenerationCaseResult>(dataset.Questions.Count);
        foreach (var question in dataset.Questions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cases.Add(await ScoreAsync(question, runOptions, cancellationToken));
        }

        var byCategory = cases
            .GroupBy(result => result.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => GenerationMetrics.FromResults([.. group]),
                StringComparer.Ordinal);
        var overall = GenerationMetrics.FromResults(cases);

        _logger.LogInformation(
            "Generation evaluation scored {QuestionCount} questions ({RunType}); "
            + "outcome match {OutcomeMatchRate}, fact coverage {FactCoverage}, "
            + "unsupported claim rate {UnsupportedClaimRate}.",
            overall.QuestionCount,
            runOptions.RunType,
            overall.OutcomeMatchRate,
            overall.MeanFactCoverage,
            overall.UnsupportedClaimRate);

        return new GenerationEvaluationReport
        {
            RunType = runOptions.RunType,
            DatasetVersion = dataset.Version,
            PipelineConfiguration = runOptions.PipelineConfiguration,
            TopK = runOptions.TopK,
            SupportThreshold = runOptions.SupportThreshold,
            Overall = overall,
            ByCategory = byCategory,
            Cases = cases,
        };
    }

    private async Task<GenerationCaseResult> ScoreAsync(
        GenerationEvaluationCase question,
        GenerationEvaluationOptions options,
        CancellationToken cancellationToken)
    {
        // Drain first: a previous question's failure must not leak its evidence
        // into this one's claim analysis.
        _evidenceRecorder?.Drain();

        QuestionResponse response;
        try
        {
            response = await _questionAnswering.AnswerAsync(
                new QuestionRequest
                {
                    Question = question.Question,
                    Scope = QuestionScope.County,
                    TopK = options.TopK,
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Answering evaluation question {QuestionId} threw.", question.Id);
            return new GenerationCaseResult
            {
                Id = question.Id,
                Category = question.Category,
                Question = question.Question,
                ExpectedOutcome = question.ExpectedOutcome,
                ActualOutcome = QuestionAnswerOutcome.Failed,
                Answer = string.Empty,
                Citations = [],
                EvidenceCount = 0,
                Facts = [],
                FactCoverage = question.ExpectedFacts.Count == 0 ? null : 0d,
                CitationTitlesMatched = null,
                Error = exception.Message,
            };
        }

        var evidence = _evidenceRecorder?.Drain();
        var isAnswered = response.Outcome == QuestionAnswerOutcome.Answered;

        // Facts and claims are only meaningful against an actual answer. An
        // insufficient-evidence response is prose about what is missing, and
        // scoring it for coverage would penalize the pipeline for doing the
        // right thing.
        var facts = isAnswered
            ? FactCoverageAnalyzer.Analyze(response.Answer, question.ExpectedFacts)
            : question.ExpectedFacts.Select(fact => new FactCoverageResult
            {
                FactId = fact.Id,
                Description = fact.Description,
                IsCovered = false,
                MissingRequiredPhrases = fact.RequiredPhrases,
                MissingAnyOf = fact.AnyOfPhrases.Count > 0,
            }).ToList();

        var claims = isAnswered && evidence is not null
            ? UnsupportedClaimDetector.Analyze(response.Answer, evidence, options.SupportThreshold)
            : null;

        return new GenerationCaseResult
        {
            Id = question.Id,
            Category = question.Category,
            Question = question.Question,
            ExpectedOutcome = question.ExpectedOutcome,
            ActualOutcome = response.Outcome,
            Answer = response.Answer,
            Citations = [.. response.Citations.Select(Summarize)],
            EvidenceCount = evidence?.Count ?? 0,
            Facts = facts,
            FactCoverage = question.ExpectedFacts.Count == 0
                ? null
                : Math.Round(
                    (double)facts.Count(fact => fact.IsCovered) / question.ExpectedFacts.Count,
                    4,
                    MidpointRounding.AwayFromZero),
            CitationTitlesMatched = EvaluateCitationTitles(question, response),
            Claims = claims,
            UnsupportedClaims = claims is null
                ? []
                : [.. claims.Where(claim => !claim.IsSupported).Select(claim => claim.Claim)],
            PromptVersion = response.PromptVersion,
            ModelDeployment = response.ModelDeployment,
        };
    }

    /// <summary>
    /// True when every cited document is one the dataset expected, false when
    /// any is not, null when the question records no expectations or the
    /// response cited nothing.
    /// </summary>
    private static bool? EvaluateCitationTitles(GenerationEvaluationCase question, QuestionResponse response)
    {
        if (question.ExpectedCitationTitles.Count == 0 || response.Citations.Count == 0)
        {
            return null;
        }

        return response.Citations.All(citation =>
            question.ExpectedCitationTitles.Any(title =>
                ExpectedSourceMatcher.TitlesMatch(citation.Title, title)));
    }

    private static CitationSummary Summarize(Citation citation) => new()
    {
        Number = citation.Number,
        Source = citation.Source.ToString(),
        Title = citation.Title,
        Section = citation.Section,
        Page = citation.Page,
    };
}
