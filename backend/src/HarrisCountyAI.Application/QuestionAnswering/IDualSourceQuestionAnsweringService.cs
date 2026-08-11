namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// Answers a question from both corpora at once — the Harris County reference
/// corpus and one case's uploaded documents — so a reviewer can ask what the
/// applicant submitted versus what the county requires in a single request.
/// </summary>
/// <remarks>
/// The two corpora are never blended at retrieval time. Implementations issue
/// two separate, independently scope-filtered retrievals (county chunks under
/// <c>sourceType eq 'KnowledgeBase'</c>, case chunks under
/// <c>sourceType eq 'CaseDocument' and caseId eq '&lt;id&gt;'</c>), keep the
/// two evidence sets distinctly labeled in the prompt, and tag every resulting
/// citation with the corpus it came from.
/// </remarks>
public interface IDualSourceQuestionAnsweringService
{
    /// <summary>
    /// Compares one case's submitted documents against the county reference
    /// corpus. Returns a cited comparison, an explicit insufficient-evidence
    /// response when either side lacks evidence, or a failure outcome — never
    /// an ungrounded answer.
    /// </summary>
    Task<DualSourceQuestionResponse> CompareAsync(
        DualSourceQuestionRequest request,
        CancellationToken cancellationToken = default);
}
