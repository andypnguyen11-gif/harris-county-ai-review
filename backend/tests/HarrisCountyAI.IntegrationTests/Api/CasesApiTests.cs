using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.Api;

public class CasesApiTests : IClassFixture<SqlServerTestDatabase>, IDisposable
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public CasesApiTests(SqlServerTestDatabase database)
    {
        _factory = new TestApplicationFactory { ConnectionStringOverride = database.ConnectionString };
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Post_Creates_Case_And_Returns_201_With_Location()
    {
        var response = await _client.PostAsJsonAsync("/api/cases", new
        {
            name = "Creek Bend Development",
            workflowType = "FloodplainDevelopmentPermit",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var id = root.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.EndsWith($"/api/cases/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        Assert.Matches(new Regex(@"^HC-\d{4}-\d{4}$"), root.GetProperty("caseNumber").GetString());
        Assert.Equal("Creek Bend Development", root.GetProperty("name").GetString());
        Assert.Equal("FloodplainDevelopmentPermit", root.GetProperty("workflowType").GetString());
        Assert.Equal("New", root.GetProperty("status").GetString());
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("createdAt").GetString(), out _));
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("updatedAt").GetString(), out _));
    }

    [Fact]
    public async Task Post_Generates_Sequential_Case_Numbers()
    {
        var first = await CreateCaseAsync("Sequential One");
        var second = await CreateCaseAsync("Sequential Two");

        var firstSequence = int.Parse(first.GetProperty("caseNumber").GetString()![^4..]);
        var secondSequence = int.Parse(second.GetProperty("caseNumber").GetString()![^4..]);

        Assert.Equal(firstSequence + 1, secondSequence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_Without_Name_Returns_400_ProblemDetails(string? name)
    {
        var response = await _client.PostAsJsonAsync("/api/cases", new
        {
            name,
            workflowType = "FloodplainDevelopmentPermit",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_With_Invalid_WorkflowType_Returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/cases", new
        {
            name = "Bad Workflow",
            workflowType = "NotARealWorkflow",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_List_Returns_Created_Cases()
    {
        var created = await CreateCaseAsync("Listed Case");
        var createdId = created.GetProperty("id").GetGuid();

        var response = await _client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = body.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(createdId, ids);
    }

    [Fact]
    public async Task Get_ById_Returns_Case()
    {
        var created = await CreateCaseAsync("Fetched Case");
        var createdId = created.GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/cases/{createdId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(createdId, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Fetched Case", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_ById_Returns_404_For_Unknown_Id()
    {
        var response = await _client.GetAsync($"/api/cases/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Updates_Name_And_Status()
    {
        var created = await CreateCaseAsync("Before Update");
        var createdId = created.GetProperty("id").GetGuid();

        var response = await _client.PatchAsJsonAsync($"/api/cases/{createdId}", new
        {
            name = "After Update",
            status = "InReview",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("After Update", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("InReview", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Patch_With_Status_Only_Keeps_Name()
    {
        var created = await CreateCaseAsync("Keep This Name");
        var createdId = created.GetProperty("id").GetGuid();

        var response = await _client.PatchAsJsonAsync($"/api/cases/{createdId}", new
        {
            status = "Processing",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Keep This Name", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("Processing", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Patch_With_Invalid_Status_Returns_400()
    {
        var created = await CreateCaseAsync("Invalid Status Target");
        var createdId = created.GetProperty("id").GetGuid();

        var response = await _client.PatchAsJsonAsync($"/api/cases/{createdId}", new
        {
            status = "NotAStatus",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Patch_Unknown_Id_Returns_404()
    {
        var response = await _client.PatchAsJsonAsync($"/api/cases/{Guid.NewGuid()}", new
        {
            name = "Ghost Case",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<JsonElement> CreateCaseAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/cases", new
        {
            name,
            workflowType = "FloodplainDevelopmentPermit",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
