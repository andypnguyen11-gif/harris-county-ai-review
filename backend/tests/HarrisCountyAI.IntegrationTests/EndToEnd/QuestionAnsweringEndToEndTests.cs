using System.Net;
using System.Text.Json;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.EndToEnd;

/// <summary>
/// Grounded question answering over material this suite actually put into the
/// index: a county question answered from an ingested reference document, a
/// case question answered from an uploaded submission, and a comparison that
/// reads both at once. Retrieval is the real service with its real scope
/// filters, so the corpus separation these tests assert is the production
/// behavior rather than a fixture's arrangement.
/// </summary>
public class QuestionAnsweringEndToEndTests : EndToEndTestBase, IClassFixture<SqlServerTestDatabase>
{
    private const string CountyTitle = "Floodplain Management Regulations";

    /// <summary>Cites more sources than any of these fixtures produce; out-of-range numbers are ignored.</summary>
    private const string CitesEverything = "[1,2,3,4,5,6,7,8]";

    public QuestionAnsweringEndToEndTests(SqlServerTestDatabase database)
        : base(database)
    {
    }

    private Task<Guid> IngestCountyRegulationsAsync() =>
        IngestKnowledgeDocumentAsync(CountyTitle, FloodplainSubmission.CountyRegulationText);

    private async Task<(Guid CaseId, Guid ApplicationId)> SubmitApplicationAsync()
    {
        var caseId = await CreateCaseAsync("Question Answering Case");
        var applicationId = await SubmitAsync(
            caseId, "permit-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(id));
        return (caseId, applicationId);
    }

    private static List<JsonElement> Citations(JsonElement body) =>
        [.. body.GetProperty("citations").EnumerateArray()];

    [Fact]
    public async Task A_County_Question_Is_Answered_From_The_Ingested_Reference_Corpus()
    {
        var knowledgeDocumentId = await IngestCountyRegulationsAsync();
        LanguageModel.EnqueueContent(
            $$"""
            {"status":"answered","answer":"A completed application form and a site plan drawn to scale are required.","citations":{{CitesEverything}}}
            """);

        var body = await AskSuccessfullyAsync(new
        {
            question = "What must a development permit application include?",
        });

        Assert.Equal("Answered", body.GetProperty("outcome").GetString());
        Assert.Equal("corpus-qa/v2", body.GetProperty("promptVersion").GetString());

        var citations = Citations(body);
        Assert.NotEmpty(citations);
        Assert.All(citations, citation =>
        {
            Assert.Equal("County", citation.GetProperty("source").GetString());
            Assert.Equal(CountyTitle, citation.GetProperty("title").GetString());
            Assert.Equal(knowledgeDocumentId, citation.GetProperty("documentId").GetGuid());
        });

        // The passage the model was shown is the passage that was ingested.
        Assert.Contains("site plan drawn to scale", Assert.Single(LanguageModel.Requests).UserPrompt);
    }

    [Fact]
    public async Task A_Case_Question_Is_Answered_From_That_Cases_Own_Documents()
    {
        var (caseId, applicationId) = await SubmitApplicationAsync();
        LanguageModel.EnqueueContent(
            $$"""
            {"status":"answered","answer":"Robert Chen signed the application as the applicant of record.","citations":{{CitesEverything}}}
            """);

        var body = await AskSuccessfullyAsync(new
        {
            question = "Who signed this application?",
            scope = "Case",
            caseId,
        });

        Assert.Equal("Answered", body.GetProperty("outcome").GetString());
        Assert.Equal("case-qa/v2", body.GetProperty("promptVersion").GetString());

        var citations = Citations(body);
        Assert.NotEmpty(citations);
        Assert.All(citations, citation =>
        {
            Assert.Equal("Case", citation.GetProperty("source").GetString());
            Assert.Equal(applicationId, citation.GetProperty("documentId").GetGuid());
            Assert.Equal("permit-application.pdf", citation.GetProperty("title").GetString());
            Assert.Equal(JsonValueKind.Number, citation.GetProperty("page").ValueKind);
        });
    }

    [Fact]
    public async Task A_Case_Citation_Opens_The_Document_It_Points_At()
    {
        var (caseId, _) = await SubmitApplicationAsync();
        LanguageModel.EnqueueContent(
            $$"""{"status":"answered","answer":"The property address is on page 1.","citations":{{CitesEverything}}}""");

        var body = await AskSuccessfullyAsync(new
        {
            question = "What is the property address?",
            scope = "Case",
            caseId,
        });

        var citation = Citations(body)[0];
        var documentId = citation.GetProperty("documentId").GetGuid();

        // Citation navigation: the reviewer opens the cited page of the cited file.
        var content = await Reviewer.GetAsync($"/api/cases/{caseId}/documents/{documentId}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("application/pdf", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", content.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(citation.GetProperty("page").GetInt32() >= 1);
    }

    [Fact]
    public async Task A_Comparison_Reads_Both_Corpora_And_Tags_Every_Citation_With_Its_Source()
    {
        var knowledgeDocumentId = await IngestCountyRegulationsAsync();
        var (caseId, applicationId) = await SubmitApplicationAsync();

        LanguageModel.EnqueueContent(
            $$"""
            {"status":"answered","answer":"The county requires an application form and a site plan; the applicant submitted the application form.","citations":{{CitesEverything}}}
            """);

        var body = await AskSuccessfullyAsync(new
        {
            question = "Did the applicant submit everything the county requires?",
            scope = "Both",
            caseId,
        });

        Assert.Equal("Answered", body.GetProperty("outcome").GetString());
        Assert.Equal("comparison-qa/v2", body.GetProperty("promptVersion").GetString());
        Assert.True(body.GetProperty("countyEvidenceCount").GetInt32() >= 1);
        Assert.True(body.GetProperty("caseEvidenceCount").GetInt32() >= 1);

        var citations = Citations(body);
        var county = citations.Where(c => c.GetProperty("source").GetString() == "County").ToList();
        var submitted = citations.Where(c => c.GetProperty("source").GetString() == "Case").ToList();

        Assert.NotEmpty(county);
        Assert.NotEmpty(submitted);
        Assert.All(county, c => Assert.Equal(knowledgeDocumentId, c.GetProperty("documentId").GetGuid()));
        Assert.All(submitted, c => Assert.Equal(applicationId, c.GetProperty("documentId").GetGuid()));
    }

    [Fact]
    public async Task A_County_Question_Never_Retrieves_A_Cases_Uploaded_Documents()
    {
        // Case evidence is indexed; the reference corpus is empty.
        await SubmitApplicationAsync();

        var body = await AskSuccessfullyAsync(new
        {
            question = "What must a development permit application include?",
        });

        Assert.Equal("InsufficientEvidence", body.GetProperty("outcome").GetString());
        Assert.Empty(body.GetProperty("citations").EnumerateArray());

        // Nothing was retrievable, so no model call was worth making.
        Assert.Empty(LanguageModel.Requests);
    }

    [Fact]
    public async Task A_Case_Question_With_Nothing_Indexed_Reports_Insufficient_Evidence()
    {
        await IngestCountyRegulationsAsync();
        var caseId = await CreateCaseAsync("Case Without Documents");

        var body = await AskSuccessfullyAsync(new
        {
            question = "Who signed this application?",
            scope = "Case",
            caseId,
        });

        Assert.Equal("InsufficientEvidence", body.GetProperty("outcome").GetString());
        Assert.Empty(body.GetProperty("citations").EnumerateArray());
        Assert.Empty(LanguageModel.Requests);
    }

    [Fact]
    public async Task A_Comparison_With_Only_One_Side_Refuses_To_Answer()
    {
        // County material only: no case documents were ever processed.
        await IngestCountyRegulationsAsync();
        var caseId = await CreateCaseAsync("Comparison Without Submission");

        var body = await AskSuccessfullyAsync(new
        {
            question = "Did the applicant submit everything the county requires?",
            scope = "Both",
            caseId,
        });

        Assert.Equal("InsufficientEvidence", body.GetProperty("outcome").GetString());
        Assert.Equal(0, body.GetProperty("caseEvidenceCount").GetInt32());
        Assert.Empty(LanguageModel.Requests);
    }

    [Fact]
    public async Task An_Answer_That_Cites_Nothing_Is_Not_Presented_As_An_Answer()
    {
        await IngestCountyRegulationsAsync();
        LanguageModel.EnqueueContent(
            """{"status":"answered","answer":"Permits are generally approved within two weeks.","citations":[]}""");

        var body = await AskSuccessfullyAsync(new
        {
            question = "How long does a permit take?",
        });

        Assert.Equal("InsufficientEvidence", body.GetProperty("outcome").GetString());
        Assert.Empty(body.GetProperty("citations").EnumerateArray());
        Assert.DoesNotContain("two weeks", body.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task A_Model_Outage_Surfaces_As_502_Rather_Than_An_Unverifiable_Answer()
    {
        await IngestCountyRegulationsAsync();
        LanguageModel.EnqueueException(new HttpRequestException("The model deployment is unavailable."));

        var response = await AskAsync(new { question = "What must an application include?" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
