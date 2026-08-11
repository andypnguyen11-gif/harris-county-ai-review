namespace HarrisCountyAI.Application.QuestionAnswering;

/// <summary>
/// What a question is asked of: the Harris County reference corpus, or the
/// documents uploaded to one specific case. The scopes are never blended —
/// each maps to its own strictly filtered retrieval, and case-scoped
/// questions always require a case id.
/// </summary>
public enum QuestionScope
{
    /// <summary>"What does Harris County require?" — answered from the reference corpus.</summary>
    County,

    /// <summary>"What did this applicant submit?" — answered from one case's uploaded documents.</summary>
    Case,

    /// <summary>
    /// "Does what this applicant submitted meet what Harris County requires?" —
    /// answered from both corpora in one request. The two corpora are still
    /// retrieved separately, under their own scope filters, and their passages
    /// stay separately labeled and separately cited; only the answer combines
    /// them. Requires a case id, and is handled by
    /// <c>IDualSourceQuestionAnsweringService</c>.
    /// </summary>
    Both,
}
