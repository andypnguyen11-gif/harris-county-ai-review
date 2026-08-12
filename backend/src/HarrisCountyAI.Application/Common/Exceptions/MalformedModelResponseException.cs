namespace HarrisCountyAI.Application.Common.Exceptions;

/// <summary>
/// The model answered, but the answer is not something we can use: no content
/// at all, or content that does not match the response contract the prompt
/// asked for.
/// </summary>
/// <remarks>
/// Callers that can degrade gracefully should catch this and fall back to an
/// explicit "not answered" result rather than let it propagate — an
/// unverifiable answer is worse than no answer. It exists as an exception so
/// that callers which cannot degrade produce a truthful 502 instead of
/// presenting model noise as a result.
/// </remarks>
public sealed class MalformedModelResponseException : Exception, IExternalServiceException
{
    public MalformedModelResponseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <inheritdoc />
    public string ServiceName => ExternalServiceNames.LanguageModel;
}
