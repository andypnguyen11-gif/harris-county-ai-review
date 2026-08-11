using HarrisCountyAI.Application.Common.Telemetry;
using HarrisCountyAI.Infrastructure.Telemetry;
using HarrisCountyAI.UnitTests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.UnitTests.Telemetry;

public class AiRequestTelemetryLoggerTests
{
    [Fact]
    public void Logs_Single_Structured_Event_With_Request_Metadata()
    {
        var logger = new CapturingLogger<AiRequestTelemetryLogger>();
        var telemetryLogger = new AiRequestTelemetryLogger(logger);
        var caseId = Guid.NewGuid();

        telemetryLogger.LogAiRequest(new AiRequestTelemetry
        {
            RequestId = "req-123",
            UserId = "reviewer-1",
            CaseId = caseId,
            Question = "Is an elevation certificate required?",
            ModelDeployment = "gpt-4o-review",
            PromptVersion = "qa-v2",
            SearchFilters = "corpus eq 'reference'",
            RetrievedChunkIds = ["chunk-1", "chunk-2"],
            RetrievalScores = [0.91, 0.72],
            RerankingScores = [2.4, 1.1],
            LatencyMilliseconds = 1250,
            PromptTokens = 812,
            CompletionTokens = 143,
            ResponseStatus = "Answered",
        });

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        var stateValues = entry.StateValues.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("req-123", stateValues["RequestId"]);
        Assert.Equal("reviewer-1", stateValues["UserId"]);
        Assert.Equal(caseId, stateValues["CaseId"]);
        Assert.Equal("gpt-4o-review", stateValues["ModelDeployment"]);
        Assert.Equal("qa-v2", stateValues["PromptVersion"]);
        Assert.Equal("corpus eq 'reference'", stateValues["SearchFilters"]);
        Assert.Equal(1250L, stateValues["LatencyMilliseconds"]);
        Assert.Equal(812, stateValues["PromptTokens"]);
        Assert.Equal(143, stateValues["CompletionTokens"]);
        Assert.Equal("Answered", stateValues["ResponseStatus"]);
    }

    [Fact]
    public void Logs_Error_Details_For_Failed_Requests()
    {
        var logger = new CapturingLogger<AiRequestTelemetryLogger>();
        var telemetryLogger = new AiRequestTelemetryLogger(logger);

        telemetryLogger.LogAiRequest(new AiRequestTelemetry
        {
            RequestId = "req-456",
            Question = "What setbacks apply?",
            ModelDeployment = "gpt-4o-review",
            ResponseStatus = "Failed",
            Error = "Deployment timed out.",
        });

        var entry = Assert.Single(logger.Entries);
        var stateValues = entry.StateValues.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("Failed", stateValues["ResponseStatus"]);
        Assert.Equal("Deployment timed out.", stateValues["Error"]);
    }

    [Fact]
    public void Throws_For_Null_Telemetry()
    {
        var telemetryLogger = new AiRequestTelemetryLogger(new CapturingLogger<AiRequestTelemetryLogger>());

        Assert.Throws<ArgumentNullException>(() => telemetryLogger.LogAiRequest(null!));
    }
}
