using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.QuestionAnswering.Prompts;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.Common.AI;

namespace HarrisCountyAI.UnitTests.QuestionAnswering;

/// <summary>
/// Case-scoped question answering: a case question requires a case id, maps
/// onto strictly case-filtered retrieval, uses the case prompt, and keeps the
/// county path exactly as it was.
/// </summary>
public class CaseScopeQuestionAnsweringTests
{
    private static readonly Guid CaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-222222222222");

    private readonly FakeRetrievalService _retrieval = new();
    private readonly FakeLanguageModelService _languageModel = new();
    private readonly QuestionAnsweringService _service;

    public CaseScopeQuestionAnsweringTests()
    {
        _service = new QuestionAnsweringService(_retrieval, _languageModel);
    }

    private static QuestionRequest CaseQuestion(Guid? caseId = null) => new()
    {
        Question = "Who signed this application?",
        Scope = QuestionScope.Case,
        CaseId = caseId ?? CaseId,
    };

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_Case_Question_Without_A_Case_Id_Is_Rejected_Before_Retrieval(string? caseId)
    {
        var request = new QuestionRequest
        {
            Question = "Who signed this application?",
            Scope = QuestionScope.Case,
            CaseId = caseId is null ? null : Guid.Parse(caseId),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.AnswerAsync(request));

        Assert.Empty(_retrieval.Requests);
    }

    [Fact]
    public async Task A_Case_Question_Retrieves_With_Case_Scope_And_The_Case_Id()
    {
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Jane Doe signed it.","citations":[1]}""");
        _retrieval.ChunksToReturn = [FakeRetrievalService.Chunk(title: "application.pdf", page: 2)];

        await _service.AnswerAsync(CaseQuestion());

        var request = Assert.Single(_retrieval.Requests);
        Assert.Equal(SourceType.Case, request.Scope);
        Assert.Equal(CaseId, request.CaseId);
    }

    [Fact]
    public async Task A_County_Question_Retrieves_With_County_Scope_And_No_Case_Id()
    {
        _retrieval.ChunksToReturn = [];

        await _service.AnswerAsync(new QuestionRequest
        {
            Question = "What does the county require?",
            CaseId = CaseId, // Supplied but irrelevant for the default County scope.
        });

        var request = Assert.Single(_retrieval.Requests);
        Assert.Equal(SourceType.County, request.Scope);
        Assert.Null(request.CaseId);
    }

    [Fact]
    public async Task A_Case_Answer_Uses_The_Case_Prompt_And_Reports_Its_Version()
    {
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Jane Doe signed it.","citations":[1]}""");
        _retrieval.ChunksToReturn = [FakeRetrievalService.Chunk(title: "application.pdf", page: 2)];

        var response = await _service.AnswerAsync(CaseQuestion());

        Assert.Equal(QuestionAnswerOutcome.Answered, response.Outcome);
        Assert.Equal(CaseQuestionPrompt.Version, response.PromptVersion);
        var modelRequest = Assert.Single(_languageModel.Requests);
        Assert.Equal(CaseQuestionPrompt.SystemPrompt, modelRequest.SystemPrompt);
        Assert.Contains(CaseQuestionPrompt.SourcesLabel, modelRequest.UserPrompt);
        Assert.Equal(CaseQuestionPrompt.ResponseSchemaName, modelRequest.JsonResponseSchemaName);
    }

    [Fact]
    public async Task A_County_Answer_Keeps_The_Corpus_Prompt_And_Version()
    {
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A site plan is required.","citations":[1]}""");
        _retrieval.ChunksToReturn = [FakeRetrievalService.Chunk()];

        var response = await _service.AnswerAsync(
            new QuestionRequest { Question = "What does the county require?" });

        Assert.Equal(GroundedQuestionPrompt.Version, response.PromptVersion);
        var modelRequest = Assert.Single(_languageModel.Requests);
        Assert.Equal(GroundedQuestionPrompt.SystemPrompt, modelRequest.SystemPrompt);
    }

    [Fact]
    public async Task A_Case_Citation_Carries_The_Document_And_Page()
    {
        var documentId = Guid.NewGuid();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Jane Doe signed page 2 of the application.","citations":[1]}""");
        _retrieval.ChunksToReturn =
        [
            FakeRetrievalService.Chunk(
                documentIdValue: documentId.ToString(),
                title: "application.pdf",
                section: null,
                page: 2,
                sourceUrl: null),
        ];

        var response = await _service.AnswerAsync(CaseQuestion());

        var citation = Assert.Single(response.Citations);
        Assert.Equal(documentId, citation.DocumentId);
        Assert.Equal("application.pdf", citation.Title);
        Assert.Equal(2, citation.Page);
        Assert.Null(citation.SourceUrl);
    }

    [Fact]
    public async Task No_Case_Evidence_Reports_Insufficient_Evidence_Without_A_Model_Call()
    {
        _retrieval.ChunksToReturn = [];

        var response = await _service.AnswerAsync(CaseQuestion());

        Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, response.Outcome);
        Assert.Contains("case's submitted documents", response.Answer);
        Assert.Empty(_languageModel.Requests);
    }

    [Fact]
    public async Task A_Case_Citation_Is_Tagged_As_Case_Sourced()
    {
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Jane Doe signed it.","citations":[1]}""");
        _retrieval.ChunksToReturn = [FakeRetrievalService.Chunk(title: "application.pdf")];

        var response = await _service.AnswerAsync(CaseQuestion());

        Assert.Equal(SourceType.Case, Assert.Single(response.Citations).Source);
    }

    [Fact]
    public async Task A_County_Citation_Is_Tagged_As_County_Sourced()
    {
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A site plan is required.","citations":[1]}""");
        _retrieval.ChunksToReturn = [FakeRetrievalService.Chunk()];

        var response = await _service.AnswerAsync(
            new QuestionRequest { Question = "What does the county require?" });

        Assert.Equal(SourceType.County, Assert.Single(response.Citations).Source);
    }

    [Fact]
    public async Task A_Both_Scoped_Question_Is_Refused_Rather_Than_Silently_Narrowed()
    {
        // Both belongs to the dual-source path; answering it here would quietly
        // return a single-corpus answer to a comparison question.
        var request = new QuestionRequest
        {
            Question = "Does the submission meet the county requirements?",
            Scope = QuestionScope.Both,
            CaseId = CaseId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.AnswerAsync(request));

        Assert.Empty(_retrieval.Requests);
    }

    [Fact]
    public async Task An_Uncited_Case_Answer_Is_Downgraded_To_Insufficient_Evidence()
    {
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Someone probably signed it.","citations":[]}""");
        _retrieval.ChunksToReturn = [FakeRetrievalService.Chunk(title: "application.pdf")];

        var response = await _service.AnswerAsync(CaseQuestion());

        Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, response.Outcome);
        Assert.Empty(response.Citations);
    }
}
