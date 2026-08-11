using HarrisCountyAI.Api.Contracts.Questions;
using HarrisCountyAI.Application.QuestionAnswering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HarrisCountyAI.Api.Controllers;

/// <summary>
/// Grounded question answering over the Harris County reference corpus,
/// scoped by case id over a single case's uploaded documents, or — with the
/// Both scope — over each of them separately in one comparison request.
/// </summary>
[ApiController]
[Route("api/questions")]
public class QuestionsController : ControllerBase
{
    private readonly IQuestionAnsweringService _questionAnswering;
    private readonly IDualSourceQuestionAnsweringService _dualSourceQuestionAnswering;

    public QuestionsController(
        IQuestionAnsweringService questionAnswering,
        IDualSourceQuestionAnsweringService dualSourceQuestionAnswering)
    {
        _questionAnswering = questionAnswering;
        _dualSourceQuestionAnswering = dualSourceQuestionAnswering;
    }

    /// <summary>
    /// Answers a question from the reference corpus (the default County
    /// scope), from one case's uploaded documents (scope Case with a case id),
    /// or from both at once (scope Both with a case id), which compares what
    /// the applicant submitted against what the county requires. Returns the
    /// answer with citations, or an explicit insufficient-evidence response; a
    /// technical failure surfaces as 502 rather than an unverifiable answer.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<QuestionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<DualSourceQuestionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Ask(
        [FromBody] AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new ModelStateDictionary();

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            errors.AddModelError("question", "A question is required.");
        }
        else if (request.Question.Length > QuestionRequest.MaxQuestionLength)
        {
            errors.AddModelError(
                "question",
                $"The question must be at most {QuestionRequest.MaxQuestionLength} characters.");
        }

        var scope = QuestionScope.County;
        if (!string.IsNullOrWhiteSpace(request.Scope)
            && (!Enum.TryParse(request.Scope, ignoreCase: true, out scope) || !Enum.IsDefined(scope)))
        {
            errors.AddModelError(
                "scope",
                $"Scope must be one of: {string.Join(", ", Enum.GetNames<QuestionScope>())}.");
        }
        else if (scope != QuestionScope.County && (request.CaseId is null || request.CaseId == Guid.Empty))
        {
            errors.AddModelError(
                "caseId",
                scope == QuestionScope.Both
                    ? "A case id is required for comparison questions."
                    : "A case id is required for case-scoped questions.");
        }

        if (!errors.IsValid)
        {
            return ValidationProblem(errors);
        }

        return scope == QuestionScope.Both
            ? await CompareAsync(request.Question!, request.CaseId!.Value, cancellationToken)
            : await AnswerSingleScopeAsync(request.Question!, scope, request.CaseId, cancellationToken);
    }

    private async Task<IActionResult> AnswerSingleScopeAsync(
        string question,
        QuestionScope scope,
        Guid? caseId,
        CancellationToken cancellationToken)
    {
        var response = await _questionAnswering.AnswerAsync(
            new QuestionRequest
            {
                Question = question,
                Scope = scope,
                CaseId = scope == QuestionScope.Case ? caseId : null,
            },
            cancellationToken);

        return response.Outcome == QuestionAnswerOutcome.Failed
            ? UnansweredProblem(response.Answer)
            : Ok(response);
    }

    private async Task<IActionResult> CompareAsync(
        string question,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        var response = await _dualSourceQuestionAnswering.CompareAsync(
            new DualSourceQuestionRequest
            {
                Question = question,
                CaseId = caseId,
            },
            cancellationToken);

        return response.Outcome == QuestionAnswerOutcome.Failed
            ? UnansweredProblem(response.Answer)
            : Ok(response);
    }

    private IActionResult UnansweredProblem(string detail) => Problem(
        title: "The question could not be answered.",
        detail: detail,
        statusCode: StatusCodes.Status502BadGateway);
}
