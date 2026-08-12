using HarrisCountyAI.Application.Common.Telemetry;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.Infrastructure.Telemetry;

/// <summary>
/// Writes AI request telemetry as a single structured log event, so the
/// configured logging providers (console JSON, Application Insights) carry
/// every field as a queryable property.
/// </summary>
public sealed class AiRequestTelemetryLogger : IAiRequestTelemetryLogger
{
    private readonly ILogger<AiRequestTelemetryLogger> _logger;

    public AiRequestTelemetryLogger(ILogger<AiRequestTelemetryLogger> logger)
    {
        _logger = logger;
    }

    public void LogAiRequest(AiRequestTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        _logger.LogInformation(
            "AI request {RequestId} for case {CaseId} by user {UserId}: status {ResponseStatus} from deployment " +
            "{ModelDeployment} (prompt {PromptVersion}) in {LatencyMilliseconds} ms; question {Question}; " +
            "filters {SearchFilters}; chunks {RetrievedChunkIds}; retrieval scores {RetrievalScores}; " +
            "reranking scores {RerankingScores}; tokens {PromptTokens}+{CompletionTokens}; error {Error}",
            telemetry.RequestId,
            telemetry.CaseId,
            telemetry.UserId,
            telemetry.ResponseStatus,
            telemetry.ModelDeployment,
            telemetry.PromptVersion,
            telemetry.LatencyMilliseconds,
            telemetry.Question,
            telemetry.SearchFilters,
            telemetry.RetrievedChunkIds,
            telemetry.RetrievalScores,
            telemetry.RerankingScores,
            telemetry.PromptTokens,
            telemetry.CompletionTokens,
            telemetry.Error);
    }
}
