using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.Api;

public class AuthenticationApiTests : IClassFixture<SqlServerTestDatabase>, IDisposable
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationApiTests(SqlServerTestDatabase database)
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
    public async Task Anonymous_Request_To_Cases_Returns_401_With_Bearer_Challenge()
    {
        var response = await _client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.Select(h => h.Scheme));
    }

    [Fact]
    public async Task Health_Endpoint_Stays_Anonymous()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DevToken_Endpoint_Issues_Token_For_AllowListed_User()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/dev-token", new
        {
            username = TestAuthentication.ReviewerUsername,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("accessToken").GetString()));
        Assert.Equal("Bearer", root.GetProperty("tokenType").GetString());
        Assert.Equal(TestAuthentication.ReviewerUsername, root.GetProperty("username").GetString());
        Assert.True(DateTimeOffset.Parse(root.GetProperty("expiresAt").GetString()!) > DateTimeOffset.UtcNow);
        Assert.Equal(["Reviewer"], root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    [Fact]
    public async Task DevToken_From_Endpoint_Grants_Access_To_Cases()
    {
        var tokenResponse = await _client.PostAsJsonAsync("/api/auth/dev-token", new
        {
            username = TestAuthentication.ReviewerUsername,
        });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        using var body = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;

        using var authenticated = _factory.CreateClient().WithToken(accessToken);
        var response = await authenticated.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DevToken_Endpoint_Returns_400_For_Unknown_User()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/dev-token", new
        {
            username = "not.a.dev.user",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DevToken_Endpoint_Returns_400_For_Missing_Username()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/dev-token", new { username = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DevToken_Endpoint_Is_Disabled_Outside_LocalDevelopment_Mode()
    {
        using var entraFactory = new TestApplicationFactory
        {
            SettingOverrides =
            {
                ["Authentication:Mode"] = "EntraId",
                ["Authentication:EntraId:Authority"] = "https://login.microsoftonline.com/common/v2.0",
                ["Authentication:EntraId:Audience"] = "api://harris-county-ai",
            },
        };
        using var client = entraFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/dev-token", new
        {
            username = TestAuthentication.ReviewerUsername,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Token_With_Wrong_Signature_Is_Rejected()
    {
        var forged = TestAuthentication.CreateToken(
            signingKey: "some-other-signing-key-0123456789-0123456789");

        using var client = _factory.CreateClient().WithToken(forged);
        var response = await client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_Token_Is_Rejected()
    {
        var expired = TestAuthentication.CreateToken(lifetime: TimeSpan.FromMinutes(-5));

        using var client = _factory.CreateClient().WithToken(expired);
        var response = await client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_With_Wrong_Issuer_Is_Rejected()
    {
        var wrongIssuer = TestAuthentication.CreateToken(issuer: "SomeOtherIssuer");

        using var client = _factory.CreateClient().WithToken(wrongIssuer);
        var response = await client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_With_Wrong_Audience_Is_Rejected()
    {
        var wrongAudience = TestAuthentication.CreateToken(audience: "SomeOtherAudience");

        using var client = _factory.CreateClient().WithToken(wrongAudience);
        var response = await client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
