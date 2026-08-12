using System.Net;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.EndToEnd;

/// <summary>
/// What "case isolation" does and does not mean in the MVP, asserted against
/// two real cases running side by side in one host.
/// </summary>
/// <remarks>
/// <para>
/// <b>What holds:</b> evidence isolation. A case's uploaded documents, its
/// retrieved passages, and its validation reports are reachable only through
/// that case. Retrieval carries a mandatory case filter, and every case-scoped
/// route resolves its child resources within the case in the URL, so an id
/// belonging to another case is a 404 rather than a leak.
/// </para>
/// <para>
/// <b>What does not hold — a known gap:</b> there is no per-case ownership or
/// assignment model. Any caller holding a valid Reviewer token can open any
/// case. The last test in this class documents that behavior deliberately
/// rather than pretending it is enforced; see the "Known limitations" section
/// of <c>docs/testing/mvp-test-plan.md</c>.
/// </para>
/// </remarks>
public class CaseIsolationEndToEndTests : EndToEndTestBase, IClassFixture<SqlServerTestDatabase>
{
    public CaseIsolationEndToEndTests(SqlServerTestDatabase database)
        : base(database)
    {
    }

    /// <summary>Two independent cases, each with its own processed application.</summary>
    private async Task<((Guid CaseId, Guid DocumentId) First, (Guid CaseId, Guid DocumentId) Second)>
        SubmitTwoCasesAsync()
    {
        var firstCaseId = await CreateCaseAsync("Cypresswood Residence");
        var firstDocumentId = await SubmitAsync(
            firstCaseId, "cypresswood-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(id));

        var secondCaseId = await CreateCaseAsync("Bayou Ridge Residence");
        var secondDocumentId = await SubmitAsync(
            secondCaseId, "bayou-ridge-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(id));

        return ((firstCaseId, firstDocumentId), (secondCaseId, secondDocumentId));
    }

    [Fact]
    public async Task A_Case_Question_Retrieves_Only_That_Cases_Documents()
    {
        var (first, second) = await SubmitTwoCasesAsync();

        LanguageModel.EnqueueContent(
            """{"status":"answered","answer":"The applicant of record is Robert Chen.","citations":[1,2,3,4]}""");

        var body = await AskSuccessfullyAsync(new
        {
            question = "Who is the applicant of record?",
            scope = "Case",
            caseId = first.CaseId,
        });

        var citations = body.GetProperty("citations").EnumerateArray().ToList();
        Assert.NotEmpty(citations);
        Assert.All(citations, citation =>
        {
            Assert.Equal(first.DocumentId, citation.GetProperty("documentId").GetGuid());
            Assert.Equal("cypresswood-application.pdf", citation.GetProperty("title").GetString());
        });

        // The other case's document was indexed and matches the query just as
        // well; only the scope filter keeps it out.
        Assert.DoesNotContain(
            citations,
            citation => citation.GetProperty("documentId").GetGuid() == second.DocumentId);
    }

    [Fact]
    public async Task A_Document_From_Another_Case_Is_Not_Reachable_Through_This_Case()
    {
        var (first, second) = await SubmitTwoCasesAsync();

        var metadata = await Reviewer.GetAsync($"/api/cases/{first.CaseId}/documents/{second.DocumentId}");
        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);

        var content = await Reviewer.GetAsync($"/api/cases/{first.CaseId}/documents/{second.DocumentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);

        // The document list for a case never mentions another case's documents.
        var list = await Reviewer.GetAsync($"/api/cases/{first.CaseId}/documents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var ids = (await ReadJsonAsync(list)).EnumerateArray()
            .Select(document => document.GetProperty("id").GetGuid())
            .ToList();
        Assert.Equal([first.DocumentId], ids);
    }

    [Fact]
    public async Task A_Validation_Report_From_Another_Case_Is_Not_Reachable_Through_This_Case()
    {
        var (first, second) = await SubmitTwoCasesAsync();

        var report = await RunValidationAsync(second.CaseId);
        var reportId = report.GetProperty("id").GetGuid();

        var response = await Reviewer.GetAsync($"/api/cases/{first.CaseId}/validation/{reportId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_Comparison_Scoped_To_One_Case_Cannot_Reach_Another_Cases_Evidence()
    {
        var (first, second) = await SubmitTwoCasesAsync();
        await IngestKnowledgeDocumentAsync(
            "Floodplain Management Regulations", FloodplainSubmission.CountyRegulationText);

        LanguageModel.EnqueueContent(
            """{"status":"answered","answer":"The county requires an application form; this applicant submitted one.","citations":[1,2,3,4,5,6]}""");

        var body = await AskSuccessfullyAsync(new
        {
            question = "Did the applicant submit everything the county requires?",
            scope = "Both",
            caseId = first.CaseId,
        });

        var caseCitations = body.GetProperty("citations").EnumerateArray()
            .Where(citation => citation.GetProperty("source").GetString() == "Case")
            .ToList();

        Assert.NotEmpty(caseCitations);
        Assert.All(caseCitations, citation =>
            Assert.Equal(first.DocumentId, citation.GetProperty("documentId").GetGuid()));
        Assert.DoesNotContain(
            caseCitations,
            citation => citation.GetProperty("documentId").GetGuid() == second.DocumentId);
    }

    [Fact]
    public async Task Any_Reviewer_Can_Open_Any_Case_Today_Because_Cases_Have_No_Owner()
    {
        // KNOWN GAP, asserted as it actually behaves rather than as it should.
        // The MVP authorizes by role only: there is no case assignment, no
        // owning reviewer, and no per-case access check anywhere in the request
        // path. A second Reviewer identity that had nothing to do with this case
        // reads it, its documents, and its validation report in full.
        //
        // Recorded in docs/testing/mvp-test-plan.md. When per-case authorization
        // lands, this test should be replaced by one asserting 403 — its failure
        // at that point is the intended signal, not a regression.
        var (first, _) = await SubmitTwoCasesAsync();
        await RunValidationAsync(first.CaseId);

        using var otherReviewer = Factory.CreateClient().WithToken(
            TestAuthentication.CreateToken("unrelated.reviewer", ["Reviewer"]));

        Assert.Equal(
            HttpStatusCode.OK,
            (await otherReviewer.GetAsync($"/api/cases/{first.CaseId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await otherReviewer.GetAsync($"/api/cases/{first.CaseId}/documents/{first.DocumentId}/content")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await otherReviewer.GetAsync($"/api/cases/{first.CaseId}/validation")).StatusCode);
    }

    [Fact]
    public async Task An_Anonymous_Caller_Reaches_Nothing_And_A_Reviewer_Cannot_Reach_The_Knowledge_Base()
    {
        var (first, _) = await SubmitTwoCasesAsync();

        using var anonymous = Factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/cases/{first.CaseId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/cases/{first.CaseId}/documents")).StatusCode);

        // Curating the reference corpus is an Administrator's job, not a
        // Reviewer's; the role boundary that does exist is enforced.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await Reviewer.GetAsync("/api/knowledge-base/documents")).StatusCode);
    }
}
