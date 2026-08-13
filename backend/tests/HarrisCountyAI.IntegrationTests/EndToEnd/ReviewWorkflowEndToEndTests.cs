using System.Net;
using System.Text.Json;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Infrastructure.Azure.Search;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.EndToEnd;

/// <summary>
/// The reviewer's path through a floodplain development permit case, end to
/// end: create the case, upload the submission, store it in blob storage,
/// extract it, normalize it, index it for search, and read the validation
/// report. Covers a valid submission, an incomplete one, one whose values fail
/// their rules, and a document the extraction service cannot read.
/// </summary>
public class ReviewWorkflowEndToEndTests : EndToEndTestBase, IClassFixture<SqlServerTestDatabase>
{
    public ReviewWorkflowEndToEndTests(SqlServerTestDatabase database)
        : base(database)
    {
    }

    /// <summary>The complete Class II package: application, site plan, and elevation certificate.</summary>
    private async Task<(Guid CaseId, Guid ApplicationId)> SubmitCompletePackageAsync()
    {
        var caseId = await CreateCaseAsync();

        var applicationId = await SubmitAsync(
            caseId, "permit-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(id));
        await SubmitAsync(caseId, "site-plan.pdf", "SitePlan", id => FloodplainSubmission.SitePlan(id));
        await SubmitAsync(
            caseId, "elevation-certificate.pdf", "ElevationCertificate",
            FloodplainSubmission.ElevationCertificate);

        return (caseId, applicationId);
    }

    [Fact]
    public async Task A_Valid_Submission_Passes_Every_Requirement_Without_Calling_The_Model()
    {
        var (caseId, _) = await SubmitCompletePackageAsync();

        var report = await RunValidationAsync(caseId);

        var items = report.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(16, items.Count);
        Assert.All(items, item => Assert.Equal("Complete", item.GetProperty("status").GetString()));

        // The core engineering principle, asserted end to end: a submission
        // whose every requirement resolves deterministically never reaches the
        // language model. The two semantic rules gate themselves off in code.
        Assert.Empty(LanguageModel.Requests);
        var semantic = items.Where(item => item.GetProperty("validationType").GetString() == "Semantic").ToList();
        Assert.Equal(2, semantic.Count);
        Assert.All(semantic, item => Assert.StartsWith("Not applicable", item.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task An_Uploaded_Document_Is_Stored_Extracted_Normalized_And_Indexed()
    {
        var (caseId, applicationId) = await SubmitCompletePackageAsync();

        // Stored: the bytes come back through the API from Azurite.
        var content = await Reviewer.GetAsync($"/api/cases/{caseId}/documents/{applicationId}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(PdfBytes, await content.Content.ReadAsByteArrayAsync());

        // Extracted and normalized: the pipeline advanced the document's status.
        var document = await GetDocumentAsync(caseId, applicationId);
        Assert.Equal("Normalized", document.GetProperty("processingStatus").GetString());
        Assert.Contains(applicationId, Extraction.Requests);

        // Indexed: chunks are tagged as case evidence and scoped to this case.
        var indexed = SearchIndex.Documents
            .Where(chunk => (string)chunk[SearchIndexDefinition.Fields.DocumentId] == applicationId.ToString("D"))
            .ToList();
        Assert.NotEmpty(indexed);
        Assert.All(indexed, chunk =>
        {
            Assert.Equal(IndexSourceTypes.CaseDocument, chunk[SearchIndexDefinition.Fields.SourceType]);
            Assert.Equal(caseId.ToString("D"), chunk[SearchIndexDefinition.Fields.CaseId]);
            Assert.Equal("permit-application.pdf", chunk[SearchIndexDefinition.Fields.Title]);
        });
    }

    [Fact]
    public async Task A_Validation_Item_Points_At_The_Document_And_Page_A_Reviewer_Can_Open()
    {
        var (caseId, applicationId) = await SubmitCompletePackageAsync();

        var report = await RunValidationAsync(caseId);
        var ownerName = Item(report, "Owner name");

        Assert.Equal("Jane P. Smith", ownerName.GetProperty("extractedValue").GetString());
        Assert.Equal("PermitApplication", ownerName.GetProperty("documentType").GetString());
        Assert.Equal(1, ownerName.GetProperty("pageNumber").GetInt32());

        // The wire shape a reviewer's browser actually consumes: boundingBox and
        // its members must be camelCased exactly this way, or the frontend reads
        // undefined. GetProperty throws if the name is wrong, so reaching each
        // value by its exact camelCased string is itself the assertion.
        var boundingBox = ownerName.GetProperty("boundingBox");
        Assert.Equal(JsonValueKind.Object, boundingBox.ValueKind);
        Assert.Equal(1, boundingBox.GetProperty("pageNumber").GetInt32());
        Assert.Equal(0.12, boundingBox.GetProperty("x").GetDouble());
        Assert.Equal(0.34, boundingBox.GetProperty("y").GetDouble());
        Assert.Equal(0.56, boundingBox.GetProperty("width").GetDouble());
        Assert.Equal(0.07, boundingBox.GetProperty("height").GetDouble());

        // The item's document id resolves to a file the reviewer can open.
        var documentId = ownerName.GetProperty("documentId").GetGuid();
        Assert.Equal(applicationId, documentId);
        var content = await Reviewer.GetAsync($"/api/cases/{caseId}/documents/{documentId}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
    }

    [Fact]
    public async Task An_Incomplete_Submission_Reports_What_Is_Missing_And_What_Needs_A_Human()
    {
        var caseId = await CreateCaseAsync("Incomplete Submission");

        // Application only, and its certification block was left blank.
        await SubmitAsync(
            caseId, "permit-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(id, signed: false));

        var report = await RunValidationAsync(caseId);

        Assert.Equal("Complete", StatusOf(report, "Development permit application"));
        Assert.Equal("Missing", StatusOf(report, "Applicant signature"));
        Assert.Equal("Missing", StatusOf(report, "Site plan"));

        // The permit class cannot be derived from extracted data, so the absent
        // elevation certificate is a reviewer's call, not a deterministic failure.
        Assert.Equal("NeedsHumanReview", StatusOf(report, "FEMA Elevation Certificate"));

        // Everything the applicant did provide still passes.
        Assert.Equal("Complete", StatusOf(report, "Owner name"));
        Assert.Equal("Complete", StatusOf(report, "Property address"));
        Assert.Empty(LanguageModel.Requests);
    }

    [Fact]
    public async Task Values_That_Fail_Their_Rules_Are_Reported_Invalid_Rather_Than_Missing()
    {
        var caseId = await CreateCaseAsync("Unreadable Values");

        await SubmitAsync(
            caseId, "permit-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(
                id,
                applicationDate: FloodplainSubmission.UnreadableDate,
                constructionTypeChecked: false,
                buildingCodeChecked: false));

        var report = await RunValidationAsync(caseId);

        var date = Item(report, "Application date");
        Assert.Equal("Invalid", date.GetProperty("status").GetString());
        Assert.Equal(FloodplainSubmission.UnreadableDate, date.GetProperty("extractedValue").GetString());
        Assert.Equal("Deterministic", date.GetProperty("validationType").GetString());

        // A section where every box is present but none is checked is Missing,
        // which is a different reviewer action from an unreadable value.
        Assert.Equal("Missing", StatusOf(report, "Construction type selection"));
        Assert.Equal("Missing", StatusOf(report, "Building code selection"));
        Assert.Empty(LanguageModel.Requests);
    }

    [Fact]
    public async Task A_Malformed_Document_Fails_Processing_Without_Blocking_The_Rest_Of_The_Case()
    {
        var caseId = await CreateCaseAsync("Malformed Upload");

        // A readable application, then a file the extraction service chokes on.
        await SubmitAsync(
            caseId, "permit-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(id));

        var corruptId = await UploadAsync(caseId, "site-plan.pdf", "SitePlan");
        Extraction.ExtractException = new InvalidOperationException(
            "The file could not be analyzed: unexpected end of stream.");
        var failure = await ProcessExpectingFailureAsync(caseId, corruptId);
        Extraction.ExtractException = null;

        Assert.Contains("could not be analyzed", failure);

        // The document records the failure and is not indexed as evidence.
        var document = await GetDocumentAsync(caseId, corruptId);
        Assert.Equal("Failed", document.GetProperty("processingStatus").GetString());
        Assert.DoesNotContain(
            SearchIndex.Documents,
            chunk => (string)chunk[SearchIndexDefinition.Fields.DocumentId] == corruptId.ToString("D"));

        // Review continues: the readable document still validates, and the
        // unreadable one surfaces as an unmet document requirement.
        var report = await RunValidationAsync(caseId);
        Assert.Equal("Complete", StatusOf(report, "Development permit application"));
        Assert.Equal("Missing", StatusOf(report, "Site plan"));
    }

    [Fact]
    public async Task Reprocessing_A_Document_Replaces_Its_Evidence_Instead_Of_Duplicating_It()
    {
        var caseId = await CreateCaseAsync("Reprocessed Submission");
        var applicationId = await SubmitAsync(
            caseId, "permit-application.pdf", "PermitApplication",
            id => FloodplainSubmission.PermitApplication(id, signed: false));

        var beforeReport = await RunValidationAsync(caseId);
        Assert.Equal("Missing", StatusOf(beforeReport, "Applicant signature"));
        var chunksBefore = ChunkCountFor(applicationId);

        // The applicant returns a signed copy; the same document is reprocessed.
        Extraction.Script(applicationId, id => FloodplainSubmission.PermitApplication(id));
        await ProcessAsync(caseId, applicationId);

        var afterReport = await RunValidationAsync(caseId);
        Assert.Equal("Complete", StatusOf(afterReport, "Applicant signature"));
        Assert.Equal(chunksBefore, ChunkCountFor(applicationId));

        // The earlier report is kept as history; the latest one is the signed run.
        var latest = await Reviewer.GetAsync($"/api/cases/{caseId}/validation");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal(
            afterReport.GetProperty("id").GetGuid(),
            (await ReadJsonAsync(latest)).GetProperty("id").GetGuid());

        var earlier = await Reviewer.GetAsync(
            $"/api/cases/{caseId}/validation/{beforeReport.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, earlier.StatusCode);
    }

    private int ChunkCountFor(Guid documentId) => SearchIndex.Documents
        .Count(chunk => (string)chunk[SearchIndexDefinition.Fields.DocumentId] == documentId.ToString("D"));
}
