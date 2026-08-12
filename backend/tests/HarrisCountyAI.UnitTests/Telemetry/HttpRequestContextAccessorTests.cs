using System.Security.Claims;
using HarrisCountyAI.Api.Middleware;
using HarrisCountyAI.Api.Telemetry;
using Microsoft.AspNetCore.Http;

namespace HarrisCountyAI.UnitTests.Telemetry;

/// <summary>
/// Reads the correlation id and caller identity off the ambient request for AI
/// telemetry. Every member has to tolerate the absence of a request, because
/// the AI services this feeds also run under the offline evaluation harness.
/// </summary>
public class HttpRequestContextAccessorTests
{
    private static HttpRequestContextAccessor AccessorFor(HttpContext? context) =>
        new(new HttpContextAccessor { HttpContext = context });

    private static DefaultHttpContext ContextWith(params Claim[] claims)
    {
        var context = new DefaultHttpContext();
        if (claims.Length > 0)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
        }

        return context;
    }

    [Fact]
    public void The_Correlation_Id_Comes_From_The_Middleware_Item()
    {
        var context = ContextWith();
        context.Items[CorrelationIdMiddleware.ItemKey] = "correlation-123";

        Assert.Equal("correlation-123", AccessorFor(context).CorrelationId);
    }

    [Fact]
    public void There_Is_No_Correlation_Id_Outside_A_Request()
    {
        Assert.Null(AccessorFor(null).CorrelationId);
        Assert.Null(AccessorFor(null).UserId);
    }

    [Fact]
    public void There_Is_No_Correlation_Id_Before_The_Middleware_Has_Run()
    {
        Assert.Null(AccessorFor(ContextWith()).CorrelationId);
    }

    [Fact]
    public void An_Anonymous_Request_Has_No_User_Id()
    {
        var context = ContextWith();
        context.Items[CorrelationIdMiddleware.ItemKey] = "correlation-123";

        Assert.Null(AccessorFor(context).UserId);
    }

    [Fact]
    public void The_Entra_Object_Id_Wins_Over_Every_Other_Claim()
    {
        // "oid" survives a username change, so it is the most stable handle on
        // a person and is preferred wherever it exists.
        var context = ContextWith(
            new Claim("preferred_username", "reviewer@example.gov"),
            new Claim("sub", "subject-id"),
            new Claim("oid", "object-id"));

        Assert.Equal("object-id", AccessorFor(context).UserId);
    }

    [Fact]
    public void The_Subject_Claim_Is_Used_When_There_Is_No_Object_Id()
    {
        var context = ContextWith(
            new Claim("preferred_username", "reviewer@example.gov"),
            new Claim("sub", "subject-id"));

        Assert.Equal("subject-id", AccessorFor(context).UserId);
    }

    [Fact]
    public void The_Mapped_NameIdentifier_Claim_Is_Understood()
    {
        // JwtBearer maps "sub" onto NameIdentifier when inbound claim mapping
        // is left enabled, so the accessor has to recognise both spellings.
        var context = ContextWith(new Claim(ClaimTypes.NameIdentifier, "mapped-subject"));

        Assert.Equal("mapped-subject", AccessorFor(context).UserId);
    }

    [Fact]
    public void The_Username_Is_Used_As_A_Last_Resort()
    {
        var context = ContextWith(new Claim("preferred_username", "reviewer@example.gov"));

        Assert.Equal("reviewer@example.gov", AccessorFor(context).UserId);
    }

    [Fact]
    public void A_Blank_Claim_Is_Skipped_Rather_Than_Reported()
    {
        var context = ContextWith(
            new Claim("oid", "   "),
            new Claim("sub", "subject-id"));

        Assert.Equal("subject-id", AccessorFor(context).UserId);
    }

    [Fact]
    public void An_Authenticated_User_With_No_Identifying_Claim_Reports_Nothing()
    {
        var context = ContextWith(new Claim("roles", "Reviewer"));

        Assert.Null(AccessorFor(context).UserId);
    }
}
