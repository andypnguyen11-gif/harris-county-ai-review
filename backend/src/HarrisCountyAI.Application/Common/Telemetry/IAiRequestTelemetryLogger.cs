namespace HarrisCountyAI.Application.Common.Telemetry;

/// <summary>
/// Records the metadata of an AI question-answering request. Question-answering
/// handlers call this once per request, whether it succeeded or failed.
/// </summary>
public interface IAiRequestTelemetryLogger
{
    void LogAiRequest(AiRequestTelemetry telemetry);
}
