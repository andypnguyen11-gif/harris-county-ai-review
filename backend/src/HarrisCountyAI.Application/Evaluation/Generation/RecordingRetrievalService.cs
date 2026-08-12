using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.Evaluation.Generation;

/// <summary>
/// Captures the evidence a question-answering run was actually given.
/// </summary>
/// <remarks>
/// <see cref="QuestionAnswering.QuestionResponse"/> carries citations but not
/// passage text, and unsupported-claim detection needs the text. Re-running
/// retrieval afterwards would not do: a second query can return different
/// chunks, and the evaluation would then be scoring the answer against evidence
/// the model never saw. Decorating the retrieval the pipeline itself used is
/// the only way to be sure.
/// </remarks>
public interface IEvaluationEvidenceRecorder
{
    /// <summary>
    /// Returns the chunks retrieved since the last call and clears the buffer,
    /// so each evaluation question starts from empty.
    /// </summary>
    IReadOnlyList<RetrievedChunk> Drain();
}

/// <summary>
/// An <see cref="IRetrievalService"/> decorator that forwards every request to
/// the real service and remembers what came back.
/// </summary>
public sealed class RecordingRetrievalService : IRetrievalService, IEvaluationEvidenceRecorder
{
    private readonly IRetrievalService _inner;
    private readonly List<RetrievedChunk> _recorded = [];
    private readonly Lock _gate = new();

    public RecordingRetrievalService(IRetrievalService inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        var chunks = await _inner.RetrieveAsync(request, cancellationToken);
        lock (_gate)
        {
            // Dual-source answering retrieves twice for one question; both
            // retrievals are evidence for the same answer, so append.
            _recorded.AddRange(chunks);
        }

        return chunks;
    }

    public IReadOnlyList<RetrievedChunk> Drain()
    {
        lock (_gate)
        {
            var drained = _recorded.ToArray();
            _recorded.Clear();
            return drained;
        }
    }
}
