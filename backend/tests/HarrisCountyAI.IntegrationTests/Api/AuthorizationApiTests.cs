using System.Net;
using HarrisCountyAI.IntegrationTests.Persistence;

namespace HarrisCountyAI.IntegrationTests.Api;

public class AuthorizationApiTests : IClassFixture<SqlServerTestDatabase>, IDisposable
{
    private readonly TestApplicationFactory _factory;

    public AuthorizationApiTests(SqlServerTestDatabase database)
    {
        _factory = new TestApplicationFactory { ConnectionStringOverride = database.ConnectionString };
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClientWithRoles(params string[] roles) =>
        _factory.CreateClient().WithToken(TestAuthentication.CreateToken(roles: roles));

    [Fact]
    public async Task Reviewer_Gets_403_On_Admin_Endpoint()
    {
        using var client = CreateClientWithRoles("Reviewer");

        var response = await client.GetAsync("/api/authorization-probes/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_Gets_200_On_Admin_Endpoint()
    {
        using var client = CreateClientWithRoles("Administrator");

        var response = await client.GetAsync("/api/authorization-probes/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Gets_401_Not_403_On_Admin_Endpoint()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/authorization-probes/admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reviewer_Gets_200_On_Reviewer_Endpoint()
    {
        using var client = CreateClientWithRoles("Reviewer");

        var response = await client.GetAsync("/api/authorization-probes/reviewer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_Satisfies_Reviewer_Policy_On_Cases()
    {
        using var client = CreateClientWithRoles("Administrator");

        var response = await client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_User_Without_Roles_Gets_403_On_Cases()
    {
        using var client = CreateClientWithRoles();

        var response = await client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Fallback_Policy_Rejects_Anonymous_On_Unattributed_Endpoint()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/authorization-probes/fallback");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Fallback_Policy_Accepts_Any_Authenticated_User()
    {
        using var client = CreateClientWithRoles();

        var response = await client.GetAsync("/api/authorization-probes/fallback");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Remains_Anonymous_Under_Fallback_Policy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The policies above are enforced on the real controllers by the attributes
    // applied when this branch merged. The cases below pin that enforcement per
    // endpoint, so a controller added later without an attribute is caught by a
    // failing test rather than silently falling back to authenticated-only.

    private const string SomeCase = "/api/cases/11111111-1111-1111-1111-111111111111";

    public static TheoryData<string> ReviewerEndpoints() =>
    [
        "/api/cases",
        $"{SomeCase}/documents",
        $"{SomeCase}/validation",
    ];

    public static TheoryData<string> AdministratorEndpoints() =>
    [
        "/api/knowledge-base/documents",
    ];

    [Theory]
    [MemberData(nameof(ReviewerEndpoints))]
    [MemberData(nameof(AdministratorEndpoints))]
    public async Task Protected_Endpoint_Rejects_Anonymous(string endpoint)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ReviewerEndpoints))]
    [MemberData(nameof(AdministratorEndpoints))]
    public async Task Protected_Endpoint_Rejects_Authenticated_User_Without_Roles(string endpoint)
    {
        using var client = CreateClientWithRoles();

        var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ReviewerEndpoints))]
    public async Task Reviewer_Endpoint_Allows_Reviewer(string endpoint)
    {
        using var client = CreateClientWithRoles("Reviewer");

        var response = await client.GetAsync(endpoint);

        // The request reaches the controller; the resource itself may well be a
        // 404, which is exactly what distinguishes "allowed through" from "denied".
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdministratorEndpoints))]
    public async Task Administrator_Endpoint_Denies_Reviewer(string endpoint)
    {
        using var client = CreateClientWithRoles("Reviewer");

        var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdministratorEndpoints))]
    public async Task Administrator_Endpoint_Allows_Administrator(string endpoint)
    {
        using var client = CreateClientWithRoles("Administrator");

        var response = await client.GetAsync(endpoint);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Question_Answering_Requires_Reviewer()
    {
        using var anonymous = _factory.CreateClient();
        using var roleless = CreateClientWithRoles();

        // Authorization runs before model binding, so an empty body is enough to
        // observe the policy decision.
        var anonymousResponse = await anonymous.PostAsync("/api/questions", null);
        var rolelessResponse = await roleless.PostAsync("/api/questions", null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rolelessResponse.StatusCode);
    }

    [Fact]
    public async Task Retrieval_Debug_Endpoint_Requires_Administrator()
    {
        using var anonymous = _factory.CreateClient();
        using var reviewer = CreateClientWithRoles("Reviewer");

        var anonymousResponse = await anonymous.PostAsync("/api/debug/retrieval", null);
        var reviewerResponse = await reviewer.PostAsync("/api/debug/retrieval", null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reviewerResponse.StatusCode);
    }
}
