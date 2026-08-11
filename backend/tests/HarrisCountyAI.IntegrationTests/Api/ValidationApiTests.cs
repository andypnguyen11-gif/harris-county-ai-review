using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.Api;

public class ValidationApiTests : IClassFixture<SqlServerTestDatabase>, IDisposable
{
    private readonly SqlServerTestDatabase _database;
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ValidationApiTests(SqlServerTestDatabase database)
    {
        _database = database;
        _factory = new TestApplicationFactory { ConnectionStringOverride = database.ConnectionString };
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Guid> CreateCaseAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/cases", new
        {
            name = "Validation Case",
            workflowType = "FloodplainDevelopmentPermit",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Seeds an extracted permit application whose fields satisfy the workflow's application rules.</summary>
    private async Task<NormalizedDocument> SeedPermitApplicationAsync(Guid caseId)
    {
        var document = new NormalizedDocument
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            CaseId = caseId,
            DocumentType = DocumentType.PermitApplication,
            RawText = "Residential Development Permit Application",
            Pages = [new DocumentPage { Id = Guid.NewGuid(), PageNumber = 1, Text = "Residential Development Permit Application" }],
            Fields =
            [
                TextField("ADDRESS", "4732 Cypresswood Dr, Spring, TX 77379"),
                TextField("HCAD ACCOUNT NUMBER", "1234567890123"),
                TextField("OWNER NAME", "Jane P. Smith"),
                TextField("PRINT NAME", "Robert Chen"),
                TextField("Initials", "RC"),
                new DocumentField { Id = Guid.NewGuid(), Name = "SIGNATURE (APPLICANT)", Kind = FieldKind.Signature, IsSigned = true, PageNumber = 1 },
                new DocumentField { Id = Guid.NewGuid(), Name = "DATE", Value = "06/01/2026", Kind = FieldKind.Date, PageNumber = 1 },
                Checkbox("Single Family Dwelling (includes garage)", isChecked: true),
                Checkbox("Existing Driveway (no new construction or expansion)", isChecked: true),
                Checkbox("Public Water & Sewer System", isChecked: true),
                Checkbox("2006 IRC", isChecked: true),
            ],
            CreatedAt = DateTime.UtcNow,
        };

        await using var context = _database.CreateContext();
        context.NormalizedDocuments.Add(document);
        await context.SaveChangesAsync();
        return document;
    }

    private static DocumentField TextField(string name, string value) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Value = value,
        Kind = FieldKind.Text,
        Confidence = 0.95,
        PageNumber = 1,
    };

    private static DocumentField Checkbox(string name, bool isChecked) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Kind = FieldKind.Checkbox,
        IsChecked = isChecked,
        PageNumber = 1,
    };

    private async Task<JsonElement> RunValidationAsync(Guid caseId)
    {
        var response = await _client.PostAsync($"/api/cases/{caseId}/validation", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    [Fact]
    public async Task Post_Runs_Validation_And_Returns_201_With_Report()
    {
        var caseId = await CreateCaseAsync();
        var application = await SeedPermitApplicationAsync(caseId);

        var response = await _client.PostAsync($"/api/cases/{caseId}/validation", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var reportId = root.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, reportId);
        Assert.Equal(caseId, root.GetProperty("caseId").GetGuid());
        Assert.Equal("FloodplainDevelopmentPermit", root.GetProperty("workflowType").GetString());
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("createdAt").GetString(), out _));

        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith(
            $"/api/cases/{caseId}/validation/{reportId}",
            response.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);

        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(16, items.Count);

        // The two semantic rules run through the same report; neither applies to
        // this submission (no narrative description, no Accessory Building/Other
        // box), so both resolve deterministically without a model call.
        var semanticItems = items.Where(i => i.GetProperty("validationType").GetString() == "Semantic").ToList();
        Assert.Equal(2, semanticItems.Count);
        Assert.All(semanticItems, i => Assert.Equal("Complete", i.GetProperty("status").GetString()));
        Assert.All(semanticItems, i => Assert.StartsWith("Not applicable", i.GetProperty("message").GetString()));

        // The application itself was extracted, so its rules pass with evidence.
        var ownerName = items.Single(i => i.GetProperty("requirement").GetString() == "Owner name");
        Assert.Equal("Complete", ownerName.GetProperty("status").GetString());
        Assert.Equal("Deterministic", ownerName.GetProperty("validationType").GetString());
        Assert.Equal("Jane P. Smith", ownerName.GetProperty("extractedValue").GetString());
        Assert.Equal(application.DocumentId, ownerName.GetProperty("documentId").GetGuid());
        Assert.Equal("PermitApplication", ownerName.GetProperty("documentType").GetString());
        Assert.Equal(1, ownerName.GetProperty("pageNumber").GetInt32());

        // No site plan was submitted, so its document rule reports Missing.
        var sitePlan = items.Single(i => i.GetProperty("requirement").GetString() == "Site plan");
        Assert.Equal("Missing", sitePlan.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, sitePlan.GetProperty("documentId").ValueKind);

        // The elevation certificate cannot be required deterministically.
        var elevationCertificate = items.Single(i => i.GetProperty("requirement").GetString() == "FEMA Elevation Certificate");
        Assert.Equal("NeedsHumanReview", elevationCertificate.GetProperty("status").GetString());

        // Items keep the workflow's rule order.
        Assert.Equal("Development permit application", items[0].GetProperty("requirement").GetString());
    }

    [Fact]
    public async Task Post_To_Unknown_Case_Returns_404_ProblemDetails()
    {
        var response = await _client.PostAsync($"/api/cases/{Guid.NewGuid()}/validation", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_Returns_404_ProblemDetails_Before_First_Run()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.GetAsync($"/api/cases/{caseId}/validation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_For_Unknown_Case_Returns_404_ProblemDetails()
    {
        var response = await _client.GetAsync($"/api/cases/{Guid.NewGuid()}/validation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_Returns_The_Latest_Report()
    {
        var caseId = await CreateCaseAsync();
        var first = await RunValidationAsync(caseId);
        await SeedPermitApplicationAsync(caseId);
        var second = await RunValidationAsync(caseId);
        Assert.NotEqual(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());

        var response = await _client.GetAsync($"/api/cases/{caseId}/validation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(second.GetProperty("id").GetGuid(), body.RootElement.GetProperty("id").GetGuid());

        // The rerun sees the newly extracted application: its owner-name check now passes.
        var ownerName = body.RootElement.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("requirement").GetString() == "Owner name");
        Assert.Equal("Complete", ownerName.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_ById_Returns_The_Report()
    {
        var caseId = await CreateCaseAsync();
        var report = await RunValidationAsync(caseId);
        var reportId = report.GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/cases/{caseId}/validation/{reportId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(reportId, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(caseId, body.RootElement.GetProperty("caseId").GetGuid());
        Assert.Equal(16, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Get_ById_Returns_404_For_Unknown_Report()
    {
        var caseId = await CreateCaseAsync();

        var response = await _client.GetAsync($"/api/cases/{caseId}/validation/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ById_Returns_404_When_Report_Belongs_To_Another_Case()
    {
        var firstCaseId = await CreateCaseAsync();
        var secondCaseId = await CreateCaseAsync();
        var report = await RunValidationAsync(firstCaseId);
        var reportId = report.GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/cases/{secondCaseId}/validation/{reportId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
