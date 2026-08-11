using HarrisCountyAI.Api.Contracts.Questions;
using HarrisCountyAI.Application.QuestionAnswering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HarrisCountyAI.Api.Controllers;

/// <summary>
/// Grounded question answering over the Harris County reference corpus and,
/// scoped by case id, over a single case's uploaded documents.
/// </summary>
[ApiController]
[Route("api/questions")]
public class QuestionsController : ControllerBase
{
    private readonly IQuestionAnsweringService _questionAnswering;

    public QuestionsController(IQuestionAnsweringService questionAnswering)
    {
        _questionAnswering = questionAnswering;
    }

    /// <summary>
    /// Answers a question from the reference corpus (the default County
    /// scope) or, with scope Case and a case id, from that case's uploaded
    /// documents. Returns the answer with citations, or an explicit
    /// insufficient-evidence response; a technical failure surfaces as 502
    /// rather than an unverifiable answer.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<QuestionResponse>(StatusCodes.Status200OK)]
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
        else if (scope == QuestionScope.Case && (request.CaseId is null || request.CaseId == Guid.Empty))
        {
            errors.AddModelError("caseId", "A case id is required for case-scoped questions.");
        }

        if (!errors.IsValid)
        {
            return ValidationProblem(errors);
        }

        var response = await _questionAnswering.AnswerAsync(
            new QuestionRequest
            {
                Question = request.Question!,
                Scope = scope,
                CaseId = scope == QuestionScope.Case ? request.CaseId : null,
            },
            cancellationToken);

        if (response.Outcome == QuestionAnswerOutcome.Failed)
        {
            return Problem(
                title: "The question could not be answered.",
                detail: response.Answer,
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Ok(response);
    }
}
