using HarrisCountyAI.Application.Validation.Semantic;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using HarrisCountyAI.UnitTests.Common.AI;

namespace HarrisCountyAI.UnitTests.Validation.Semantic;

public class SemanticEvaluationRuleTests
{
    private const string Requirement = "Site plan sufficiency";
    private const string RequirementDescription =
        "The site plan must contain a sufficient description to locate the property.";

    private readonly FakeLanguageModelService _model = new();

    private SemanticEvaluationRule CreateRule(
        Func<ValidationContext, string?>? contentSelector = null,
        Func<ValidationContext, bool>? applicableWhen = null,
        ValidationStatus missingContentStatus = ValidationStatus.UnableToDetermine,
        string? missingContentMessage = null,
        ISemanticValidationService? service = null) =>
        new(
            Requirement,
            RequirementDescription,
            service ?? new SemanticValidationService(_model),
            DocumentType.SitePlan,
            contentSelector,
            applicableWhen,
            missingContentStatus,
            missingContentMessage);

    private static ValidationContext ContextWithSitePlan(string rawText = "Lot 4, Block 2, Cypresswood Section 3.")
    {
        var sitePlan = new NormalizedDocumentBuilder(DocumentType.SitePlan).Build();
        sitePlan.RawText = rawText;
        return NormalizedDocumentBuilder.ContextFor(sitePlan);
    }

    [Fact]
    public void Name_Identifies_The_Rule_And_Requirement()
    {
        Assert.Equal("SemanticEvaluationRule(Site plan sufficiency)", CreateRule().Name);
    }

    [Fact]
    public async Task Pass_Verdict_Yields_Complete_Semantic_Result_With_Model_Reasoning()
    {
        _model.EnqueueContent("""{"verdict": "pass", "reasoning": "The plan locates the property."}""");
        var context = ContextWithSitePlan();

        var result = await CreateRule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(Requirement, result.Requirement);
        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal("The plan locates the property.", result.Message);
        Assert.Equal(ValidationType.Semantic, result.ValidationType);
        Assert.Equal("SemanticEvaluationRule(Site plan sufficiency)", result.RuleName);
        Assert.Equal(context.Documents[0].Id, result.SourceDocumentId);
    }

    [Fact]
    public async Task Fail_Verdict_Yields_Invalid()
    {
        _model.EnqueueContent("""{"verdict": "fail", "reasoning": "The plan does not locate the property."}""");

        var result = await CreateRule().ValidateAsync(ContextWithSitePlan(), CancellationToken.None);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Equal("The plan does not locate the property.", result.Message);
        Assert.Equal(ValidationType.Semantic, result.ValidationType);
    }

    [Fact]
    public async Task NeedsHumanReview_Verdict_Yields_NeedsHumanReview()
    {
        _model.EnqueueContent("""{"verdict": "needs_human_review", "reasoning": "The plan is ambiguous."}""");

        var result = await CreateRule().ValidateAsync(ContextWithSitePlan(), CancellationToken.None);

        Assert.Equal(ValidationStatus.NeedsHumanReview, result.Status);
    }

    [Fact]
    public async Task Malformed_Model_Response_Yields_UnableToDetermine()
    {
        _model.EnqueueContent("not json at all");

        var result = await CreateRule().ValidateAsync(ContextWithSitePlan(), CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
        Assert.Equal(ValidationType.Semantic, result.ValidationType);
    }

    [Fact]
    public async Task Model_Exception_Yields_UnableToDetermine_Not_A_Crash()
    {
        _model.EnqueueException(new InvalidOperationException("model unavailable"));

        var result = await CreateRule().ValidateAsync(ContextWithSitePlan(), CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
        Assert.Equal(ValidationType.Semantic, result.ValidationType);
    }

    [Fact]
    public async Task Throwing_Service_Yields_UnableToDetermine_Semantic_Result()
    {
        var rule = CreateRule(service: new ThrowingSemanticValidationService());

        var result = await rule.ValidateAsync(ContextWithSitePlan(), CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
        Assert.Equal(ValidationType.Semantic, result.ValidationType);
        Assert.Contains("failed to run", result.Message);
    }

    [Fact]
    public async Task Empty_Context_Yields_UnableToDetermine_Without_Model_Call()
    {
        var result = await CreateRule().ValidateAsync(
            NormalizedDocumentBuilder.ContextFor(), CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
        Assert.Contains("No extracted documents", result.Message);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task Missing_Scoped_Document_Yields_UnableToDetermine_Without_Model_Call()
    {
        var application = new NormalizedDocumentBuilder(DocumentType.PermitApplication).Build();

        var result = await CreateRule().ValidateAsync(
            NormalizedDocumentBuilder.ContextFor(application), CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
        Assert.Contains("No SitePlan document", result.Message);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task Unmet_Applicability_Condition_Yields_Complete_Without_Model_Call()
    {
        var result = await CreateRule(applicableWhen: _ => false)
            .ValidateAsync(ContextWithSitePlan(), CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Contains("Not applicable", result.Message);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task Missing_Content_Yields_Configured_Status_Without_Model_Call()
    {
        var result = await CreateRule(
                contentSelector: _ => null,
                missingContentStatus: ValidationStatus.Missing,
                missingContentMessage: "No description was provided.")
            .ValidateAsync(ContextWithSitePlan(), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal("No description was provided.", result.Message);
        Assert.Equal(0, _model.CallCount);
    }

    [Fact]
    public async Task Default_Content_Selector_Sends_Scoped_Document_Raw_Text()
    {
        _model.EnqueueContent("""{"verdict": "pass", "reasoning": "OK."}""");

        await CreateRule().ValidateAsync(ContextWithSitePlan("UNIQUE-RAW-TEXT"), CancellationToken.None);

        Assert.Contains("UNIQUE-RAW-TEXT", _model.LastRequest!.UserPrompt);
        Assert.Contains(RequirementDescription, _model.LastRequest.UserPrompt);
    }

    [Fact]
    public async Task Caller_Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateRule().ValidateAsync(ContextWithSitePlan(), cancellation.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Requirement_Throws(string requirement)
    {
        Assert.Throws<ArgumentException>(() => new SemanticEvaluationRule(
            requirement, RequirementDescription, new SemanticValidationService(_model)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Requirement_Description_Throws(string description)
    {
        Assert.Throws<ArgumentException>(() => new SemanticEvaluationRule(
            Requirement, description, new SemanticValidationService(_model)));
    }

    private sealed class ThrowingSemanticValidationService : ISemanticValidationService
    {
        public Task<SemanticValidationResult> EvaluateAsync(
            SemanticValidationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("selector blew up");
    }
}
