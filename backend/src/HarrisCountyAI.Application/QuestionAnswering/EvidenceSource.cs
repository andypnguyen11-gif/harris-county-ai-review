using HarrisCountyAI.Application.Search.Retrieval;

namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// One numbered source shown to the model, paired with the corpus it was
/// retrieved from. The pairing is assigned by the service that performed the
/// retrieval — never inferred from the chunk itself and never chosen by the
/// model — so a citation's <see cref="Citation.Source"/> always reflects the
/// scope filter the passage was actually retrieved under.
/// </summary>
/// <param name="Source">The corpus the chunk came from.</param>
/// <param name="Chunk">The retrieved passage.</param>
public readonly record struct EvidenceSource(SourceType Source, RetrievedChunk Chunk);
