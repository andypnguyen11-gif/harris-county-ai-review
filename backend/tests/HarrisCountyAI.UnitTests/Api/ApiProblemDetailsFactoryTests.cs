using HarrisCountyAI.Api.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Api;

public class ApiProblemDetailsFactoryTests
{
    private static ApiProblemDetailsFactory CreateFactory()
    {
        var options = new ApiBehaviorOptions();
        options.ClientErrorMapping[404] = new ClientErrorData
        {
            Title = "Not Found",
            Link = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        };
        return new ApiProblemDetailsFactory(Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string? correlationId = "abc123")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/cases/1";
        context.TraceIdentifier = "trace-1";
        if (correlationId is not null)
        {
            context.Items[ApiProblemDetails.CorrelationIdItemKey] = correlationId;
        }

        return context;
    }

    [Fact]
    public void Every_Problem_Document_Carries_The_Correlation_Id()
    {
        var problemDetails = CreateFactory().CreateProblemDetails(CreateContext(), statusCode: 502);

        Assert.Equal("abc123", problemDetails.Extensions[ApiProblemDetails.CorrelationIdExtension]);
    }

    [Fact]
    public void The_Response_Header_Is_Used_When_Items_Has_No_Id()
    {
        var context = CreateContext(correlationId: null);
        context.Response.Headers[ApiProblemDetails.CorrelationIdHeaderName] = "from-header";

        var problemDetails = CreateFactory().CreateProblemDetails(context, statusCode: 500);

        Assert.Equal("from-header", problemDetails.Extensions[ApiProblemDetails.CorrelationIdExtension]);
    }

    [Fact]
    public void The_Trace_Identifier_Is_The_Last_Resort()
    {
        var problemDetails = CreateFactory()
            .CreateProblemDetails(CreateContext(correlationId: null), statusCode: 500);

        Assert.Equal("trace-1", problemDetails.Extensions[ApiProblemDetails.CorrelationIdExtension]);
    }

    [Fact]
    public void Framework_Defaults_For_The_Status_Are_Preserved()
    {
        var problemDetails = CreateFactory().CreateProblemDetails(CreateContext(), statusCode: 404);

        Assert.Equal("Not Found", problemDetails.Title);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", problemDetails.Type);
        Assert.Equal("trace-1", problemDetails.Extensions["traceId"]);
        Assert.Equal("/api/cases/1", problemDetails.Instance);
    }

    [Fact]
    public void An_Explicit_Title_Wins_Over_The_Status_Default()
    {
        var problemDetails = CreateFactory().CreateProblemDetails(
            CreateContext(), statusCode: 404, title: "The document was not found.");

        Assert.Equal("The document was not found.", problemDetails.Title);
    }

    [Fact]
    public void The_Status_Defaults_To_500_When_Unspecified()
    {
        var problemDetails = CreateFactory().CreateProblemDetails(CreateContext());

        Assert.Equal(StatusCodes.Status500InternalServerError, problemDetails.Status);
    }

    [Fact]
    public void Validation_Problems_Keep_Their_Errors_And_Gain_The_Correlation_Id()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("question", "A question is required.");

        var problemDetails = CreateFactory()
            .CreateValidationProblemDetails(CreateContext(), modelState);

        Assert.Equal(StatusCodes.Status400BadRequest, problemDetails.Status);
        Assert.Equal("One or more validation errors occurred.", problemDetails.Title);
        Assert.Equal("A question is required.", Assert.Single(problemDetails.Errors["question"]));
        Assert.Equal("abc123", problemDetails.Extensions[ApiProblemDetails.CorrelationIdExtension]);
    }

    [Fact]
    public void Validation_Problems_Accept_An_Explicit_Title()
    {
        var problemDetails = CreateFactory().CreateValidationProblemDetails(
            CreateContext(), new ModelStateDictionary(), title: "The upload was rejected.");

        Assert.Equal("The upload was rejected.", problemDetails.Title);
    }

    [Fact]
    public void A_Null_Model_State_Is_Rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => CreateFactory().CreateValidationProblemDetails(CreateContext(), null!));
    }
}
