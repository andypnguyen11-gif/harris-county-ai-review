using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.QuestionAnswering.Prompts;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.UnitTests.Common.AI;

namespace HarrisCountyAI.UnitTests.QuestionAnswering;

/// <summary>
/// Dual-source comparison: the two corpora are retrieved separately under
/// their own scope filters, presented to the model under distinct labels,
/// cited with the corpus each passage came from, and the fail-closed
/// insufficient-evidence behavior survives a one-sided retrieval.
/// </summary>
public class DualSourceQuestionAnsweringServiceTests
{
    private const string Question = "Did the applicant submit everything the county requires?";
    private static readonly Guid CaseId = Guid.Parse("7d9f4a31-8a20-4e5b-9b1a-444444444444");

    private readonly FakeRetrievalService _retrieval = new();
    private readonly FakeLanguageModelService _languageModel = new();
    private readonly DualSourceQuestionAnsweringService _service;

    public DualSourceQuestionAnsweringServiceTests()
    {
        _service = new DualSourceQuestionAnsweringService(_retrieval, _languageModel);
    }

    private static DualSourceQuestionRequest Request(Guid? caseId = null) => new()
    {
        Question = Question,
        CaseId = caseId ?? CaseId,
    };

    private static RetrievedChunk CountyChunk(
        string text = "A site plan is required with every development permit application.",
        string title = "Floodplain Regulations") =>
        FakeRetrievalService.Chunk(
            chunkId: "county-0001",
            documentIdValue: "0f8fad5b-d9cb-469f-a165-408319b0e0d9",
            text: text,
            title: title,
            section: "Section 4.04",
            page: 12,
            sourceUrl: "https://www.hcfcd.org/regulations");

    private static RetrievedChunk CaseChunk(
        string text = "Attached: site plan sheet 1 of 2.",
        string title = "application.pdf") =>
        FakeRetrievalService.Chunk(
            chunkId: "case-0001",
            documentIdValue: "d1d1d1d1-1111-1111-1111-d1d1d1d1d1d1",
            text: text,
            title: title,
            section: null,
            page: 3,
            sourceUrl: null);

    /// <summary>Serves distinct evidence for each corpus.</summary>
    private void GiveBothSidesEvidence()
    {
        _retrieval.ChunksByScope[SourceType.County] = [CountyChunk()];
        _retrieval.ChunksByScope[SourceType.Case] = [CaseChunk()];
    }

    [Fact]
    public async Task An_Empty_Question_Is_Rejected_Before_Retrieval()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CompareAsync(new DualSourceQuestionRequest { Question = "  ", CaseId = CaseId }));

        Assert.Empty(_retrieval.Requests);
    }

    [Fact]
    public async Task A_Comparison_Without_A_Case_Id_Is_Rejected_Before_Retrieval()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CompareAsync(Request(Guid.Empty)));

        Assert.Empty(_retrieval.Requests);
    }

    [Fact]
    public async Task Both_Corpora_Are_Retrieved_Separately_Under_Their_Own_Scope()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"The site plan was submitted.","citations":[1,2]}""");

        await _service.CompareAsync(Request());

        Assert.Equal(2, _retrieval.Requests.Count);

        var county = _retrieval.RequestFor(SourceType.County);
        Assert.Equal(Question, county.Query);
        Assert.Null(county.CaseId);

        var caseRequest = _retrieval.RequestFor(SourceType.Case);
        Assert.Equal(Question, caseRequest.Query);
        Assert.Equal(CaseId, caseRequest.CaseId);
    }

    [Fact]
    public async Task The_County_Retrieval_Never_Carries_A_Case_Id()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"The site plan was submitted.","citations":[1,2]}""");

        await _service.CompareAsync(Request());

        // The corpus has no case-scoped content; a case id on the county query
        // would be the first step toward blending the two corpora.
        Assert.All(
            _retrieval.Requests.Where(request => request.Scope == SourceType.County),
            request => Assert.Null(request.CaseId));
    }

    [Fact]
    public async Task Every_Case_Retrieval_Carries_Exactly_The_Requested_Case_Id()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"The site plan was submitted.","citations":[1,2]}""");

        await _service.CompareAsync(Request());

        Assert.All(
            _retrieval.Requests.Where(request => request.Scope == SourceType.Case),
            request => Assert.Equal(CaseId, request.CaseId));
    }

    [Fact]
    public async Task Corpus_Metadata_Filters_Apply_To_The_County_Side_Only()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"The site plan was submitted.","citations":[1,2]}""");

        await _service.CompareAsync(Request() with
        {
            PermitType = "FloodplainDevelopmentPermit",
            Department = "Engineering",
        });

        var county = _retrieval.RequestFor(SourceType.County);
        Assert.Equal("FloodplainDevelopmentPermit", county.PermitType);
        Assert.Equal("Engineering", county.Department);

        var caseRequest = _retrieval.RequestFor(SourceType.Case);
        Assert.Null(caseRequest.PermitType);
        Assert.Null(caseRequest.Department);
    }

    [Fact]
    public async Task The_Comparison_Prompt_Labels_The_Two_Evidence_Sets_Distinctly()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"The site plan was submitted.","citations":[1,2]}""");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(ComparisonPrompt.Version, response.PromptVersion);
        var modelRequest = Assert.Single(_languageModel.Requests);
        Assert.Equal(ComparisonPrompt.SystemPrompt, modelRequest.SystemPrompt);
        Assert.Equal(ComparisonPrompt.ResponseSchemaName, modelRequest.JsonResponseSchemaName);
        Assert.Contains(ComparisonPrompt.CountySourcesLabel, modelRequest.UserPrompt);
        Assert.Contains(ComparisonPrompt.CaseSourcesLabel, modelRequest.UserPrompt);
    }

    [Fact]
    public async Task Case_Text_Appears_Only_Inside_The_Applicant_Submission_Block()
    {
        _retrieval.ChunksByScope[SourceType.County] = [CountyChunk(text: "COUNTY-ONLY-TEXT")];
        _retrieval.ChunksByScope[SourceType.Case] = [CaseChunk(text: "CASE-ONLY-TEXT")];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Compared.","citations":[1,2]}""");

        await _service.CompareAsync(Request());

        var prompt = Assert.Single(_languageModel.Requests).UserPrompt;
        var countyBlock = Block(
            prompt, ComparisonPrompt.CountySourcesBeginDelimiter, ComparisonPrompt.CountySourcesEndDelimiter);
        var caseBlock = Block(
            prompt, ComparisonPrompt.CaseSourcesBeginDelimiter, ComparisonPrompt.CaseSourcesEndDelimiter);

        Assert.Contains("COUNTY-ONLY-TEXT", countyBlock);
        Assert.DoesNotContain("CASE-ONLY-TEXT", countyBlock);
        Assert.Contains("CASE-ONLY-TEXT", caseBlock);
        Assert.DoesNotContain("COUNTY-ONLY-TEXT", caseBlock);
    }

    [Fact]
    public async Task Citations_Are_Tagged_With_The_Corpus_Their_Passage_Came_From()
    {
        _retrieval.ChunksByScope[SourceType.County] = [CountyChunk(), CountyChunk(title: "Design Manual")];
        _retrieval.ChunksByScope[SourceType.Case] = [CaseChunk()];
        // Sources are numbered continuously: 1 and 2 are county, 3 is the case.
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A site plan is required and one was submitted.","citations":[1,3]}""");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.Answered, response.Outcome);
        Assert.Equal(2, response.Citations.Count);

        var countyCitation = Assert.Single(response.Citations, c => c.Source == SourceType.County);
        Assert.Equal(1, countyCitation.Number);
        Assert.Equal("Floodplain Regulations", countyCitation.Title);
        Assert.Equal("https://www.hcfcd.org/regulations", countyCitation.SourceUrl);

        var caseCitation = Assert.Single(response.Citations, c => c.Source == SourceType.Case);
        Assert.Equal(3, caseCitation.Number);
        Assert.Equal("application.pdf", caseCitation.Title);
        Assert.Equal(3, caseCitation.Page);
        Assert.Null(caseCitation.SourceUrl);
    }

    [Fact]
    public async Task Evidence_Counts_Report_Each_Corpus_Separately()
    {
        _retrieval.ChunksByScope[SourceType.County] = [CountyChunk(), CountyChunk(title: "Design Manual")];
        _retrieval.ChunksByScope[SourceType.Case] = [CaseChunk()];
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Compared.","citations":[1,3]}""");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(2, response.CountyEvidenceCount);
        Assert.Equal(1, response.CaseEvidenceCount);
    }

    [Fact]
    public async Task No_County_Evidence_Reports_Insufficient_Evidence_Without_A_Model_Call()
    {
        _retrieval.ChunksByScope[SourceType.County] = [];
        _retrieval.ChunksByScope[SourceType.Case] = [CaseChunk()];

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, response.Outcome);
        Assert.Contains("Harris County reference material", response.Answer);
        Assert.Empty(response.Citations);
        Assert.Equal(0, response.CountyEvidenceCount);
        Assert.Equal(1, response.CaseEvidenceCount);
        Assert.Empty(_languageModel.Requests);
    }

    [Fact]
    public async Task No_Case_Evidence_Reports_Insufficient_Evidence_Without_A_Model_Call()
    {
        _retrieval.ChunksByScope[SourceType.County] = [CountyChunk()];
        _retrieval.ChunksByScope[SourceType.Case] = [];

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, response.Outcome);
        Assert.Contains("submitted documents", response.Answer);
        Assert.Equal(1, response.CountyEvidenceCount);
        Assert.Equal(0, response.CaseEvidenceCount);
        Assert.Empty(_languageModel.Requests);
    }

    [Fact]
    public async Task No_Evidence_At_All_Reports_Insufficient_Evidence_Without_A_Model_Call()
    {
        _retrieval.ChunksToReturn = [];

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, response.Outcome);
        Assert.Contains("Neither", response.Answer);
        Assert.Empty(_languageModel.Requests);
    }

    [Fact]
    public async Task A_Model_Reported_Insufficient_Evidence_Is_Passed_Through_Without_Citations()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"insufficient_evidence","answer":"The sources do not cover drainage plans.","citations":[]}""");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, response.Outcome);
        Assert.Equal("The sources do not cover drainage plans.", response.Answer);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public async Task An_Uncited_Comparison_Is_Downgraded_To_Insufficient_Evidence()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"It probably meets the requirements.","citations":[]}""");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, response.Outcome);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public async Task A_Comparison_Citing_Only_Case_Sources_Is_Downgraded_To_Insufficient_Evidence()
    {
        GiveBothSidesEvidence();
        // Source 2 is the case passage; asserting a county requirement while
        // citing only the applicant's own document is exactly the confusion
        // this path exists to prevent.
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"The county requires a site plan and one was attached.","citations":[2]}""");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.InsufficientEvidence, response.Outcome);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public async Task A_Comparison_Citing_Only_County_Sources_Still_Answers()
    {
        GiveBothSidesEvidence();
        // Reporting that the submission does not show a required item is a
        // legitimate grounded comparison: an absence has no passage to cite.
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"A site plan is required; the submitted documents do not show one.","citations":[1]}""");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.Answered, response.Outcome);
        var citation = Assert.Single(response.Citations);
        Assert.Equal(SourceType.County, citation.Source);
    }

    [Fact]
    public async Task Citation_Numbers_Outside_The_Source_Range_Are_Ignored()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(
            """{"status":"answered","answer":"Compared.","citations":[1,1,0,9,"2"]}""");

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.Answered, response.Outcome);
        var citation = Assert.Single(response.Citations);
        Assert.Equal(1, citation.Number);
    }

    [Fact]
    public async Task A_Language_Model_Failure_Reports_Failed()
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueException(new HttpRequestException("model unavailable"));

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.Failed, response.Outcome);
        Assert.Empty(response.Citations);
        Assert.Equal(ComparisonPrompt.Version, response.PromptVersion);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"status": }""")]
    [InlineData("""{"answer":"no status here"}""")]
    [InlineData("""{"status":"maybe","answer":"unknown status","citations":[1]}""")]
    public async Task An_Unusable_Model_Response_Reports_Failed(string content)
    {
        GiveBothSidesEvidence();
        _languageModel.EnqueueContent(content);

        var response = await _service.CompareAsync(Request());

        Assert.Equal(QuestionAnswerOutcome.Failed, response.Outcome);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public async Task Cancellation_Propagates_Instead_Of_Becoming_A_Failed_Outcome()
    {
        GiveBothSidesEvidence();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.CompareAsync(Request(), cancellation.Token));
    }

    /// <summary>Extracts the text between two delimiters, exclusive.</summary>
    private static string Block(string text, string beginDelimiter, string endDelimiter)
    {
        var start = text.IndexOf(beginDelimiter, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Prompt is missing '{beginDelimiter}'.");
        start += beginDelimiter.Length;

        var end = text.IndexOf(endDelimiter, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Prompt is missing '{endDelimiter}'.");

        return text[start..end];
    }
}
