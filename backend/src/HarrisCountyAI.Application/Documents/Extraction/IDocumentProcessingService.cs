using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Application.Documents.Extraction;

/// <summary>
/// Orchestrates the processing pipeline for an uploaded document: download
/// from storage, extraction, normalization, persistence, and
/// processing-status bookkeeping.
/// </summary>
public interface IDocumentProcessingService
{
    /// <summary>
    /// Runs the processing pipeline for the document with
    /// <paramref name="documentId"/> and returns the persisted normalized
    /// document.
    /// </summary>
    /// <exception cref="InvalidOperationException">No document with <paramref name="documentId"/> exists.</exception>
    Task<NormalizedDocument> ProcessAsync(Guid documentId, CancellationToken cancellationToken = default);
}
