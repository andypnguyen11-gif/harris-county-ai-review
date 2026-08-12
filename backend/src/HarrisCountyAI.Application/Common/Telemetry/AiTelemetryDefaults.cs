namespace HarrisCountyAI.Application.Common.Telemetry;

/// <summary>
/// Placeholder values for the required <see cref="AiRequestTelemetry"/> fields
/// that cannot always be filled in.
/// </summary>
/// <remarks>
/// These exist so telemetry is emitted for every AI call rather than skipped
/// whenever a value is missing. A record with a placeholder still carries the
/// question, the outcome, and the evidence ids, which is what an audit needs;
/// dropping the record entirely would lose all of it. The values are
/// deliberately unmistakable so no reader confuses one for a real id.
/// </remarks>
public static class AiTelemetryDefaults
{
    /// <summary>
    /// Stands in for the correlation id when the call did not originate from an
    /// HTTP request — the offline evaluation harness and unit tests both do this.
    /// </summary>
    public const string NoRequestId = "no-http-request";

    /// <summary>
    /// Stands in for the model deployment when no model call was made, which
    /// happens when retrieval returns no evidence and the pipeline fails closed
    /// before reaching the model.
    /// </summary>
    public const string NoModelDeployment = "none (no model call)";
}
