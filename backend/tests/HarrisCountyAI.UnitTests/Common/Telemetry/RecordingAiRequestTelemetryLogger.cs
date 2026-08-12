using HarrisCountyAI.Application.Common.Telemetry;

namespace HarrisCountyAI.UnitTests.Common.Telemetry;

/// <summary>
/// Captures every <see cref="AiRequestTelemetry"/> record emitted, so tests can
/// assert on what an AI call actually reported.
/// </summary>
public sealed class RecordingAiRequestTelemetryLogger : IAiRequestTelemetryLogger
{
    private readonly List<AiRequestTelemetry> _records = [];

    /// <summary>Every record emitted, in call order.</summary>
    public IReadOnlyList<AiRequestTelemetry> Records => _records;

    /// <summary>The only record emitted; fails when there is not exactly one.</summary>
    public AiRequestTelemetry Single => Assert.Single(_records);

    /// <summary>When set, <see cref="LogAiRequest"/> throws this instead of recording.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public void LogAiRequest(AiRequestTelemetry telemetry)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        _records.Add(telemetry);
    }
}

/// <summary>
/// Fixed request context, standing in for the ambient HTTP request.
/// </summary>
public sealed class StubRequestContextAccessor : IRequestContextAccessor
{
    public string? CorrelationId { get; set; }

    public string? UserId { get; set; }
}
