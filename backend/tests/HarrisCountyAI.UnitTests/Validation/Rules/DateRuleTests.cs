using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.UnitTests.Validation.Rules;

public class DateRuleTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    private static DateRule Rule(bool disallowFuture = false, TimeSpan? maxAge = null) =>
        new(
            "Application date",
            "Date",
            ["Application Date", "Date Signed"],
            DocumentType.PermitApplication,
            disallowFuture,
            maxAge,
            () => FixedNow);

    [Theory]
    [InlineData("06/01/2026")]
    [InlineData("6/1/2026")]
    [InlineData("2026-06-01")]
    [InlineData("June 1, 2026")]
    public async Task Complete_When_Value_Parses_As_Date(string value)
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("Date", value, page: 1)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(value, result.ExtractedValue);
        Assert.Equal(document.Id, result.SourceDocumentId);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task Complete_When_Field_Matches_A_Name_Variant()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("DATE SIGNED", "06/01/2026")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("13/45/2026")]
    public async Task Invalid_When_Value_Does_Not_Parse(string value)
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("Date", value)
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Equal(value, result.ExtractedValue);
        Assert.Contains("could not be read as a date", result.Message);
    }

    [Fact]
    public async Task Invalid_When_Future_Date_Disallowed()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("Date", "08/12/2026")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule(disallowFuture: true).ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Contains("future", result.Message);
    }

    [Fact]
    public async Task Complete_When_Future_Date_Allowed()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("Date", "08/12/2026")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
    }

    [Fact]
    public async Task Invalid_When_Date_Older_Than_MaxAge()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("Date", "01/01/2025")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule(maxAge: TimeSpan.FromDays(180)).ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Contains("older than", result.Message);
    }

    [Fact]
    public async Task Missing_When_Field_Not_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("ADDRESS", "123 Main St")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
    }

    [Fact]
    public async Task Missing_When_Field_Present_But_Empty()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("Date", "  ")
            .Build();
        var context = NormalizedDocumentBuilder.ContextFor(document);

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(document.Id, result.SourceDocumentId);
    }

    [Fact]
    public async Task UnableToDetermine_When_Scoped_Document_Type_Absent()
    {
        var context = NormalizedDocumentBuilder.ContextFor(
            new NormalizedDocumentBuilder(DocumentType.SitePlan).Build());

        var result = await Rule().ValidateAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.UnableToDetermine, result.Status);
    }

    private static BoundingBox RegionOn(int pageNumber, double x) => new()
    {
        PageNumber = pageNumber,
        X = x,
        Y = 0.3,
        Width = 0.2,
        Height = 0.03,
    };

    [Fact]
    public async Task Reports_The_Value_Region_For_A_Valid_Date()
    {
        var valueBox = RegionOn(2, 0.5);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("application date", "1/15/2026", page: 2, keyBox: RegionOn(2, 0.1), valueBox: valueBox)
            .Build();
        var rule = new DateRule("Application date", "application date");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Complete, result.Status);
        Assert.Equal(valueBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Value_Region_For_An_Unparseable_Date()
    {
        var valueBox = RegionOn(2, 0.5);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("application date", "not a date", page: 2, keyBox: RegionOn(2, 0.1), valueBox: valueBox)
            .Build();
        var rule = new DateRule("Application date", "application date");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Equal(valueBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_The_Key_Region_For_An_Empty_Date()
    {
        var keyBox = RegionOn(2, 0.1);
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithDateField("application date", null, page: 2, keyBox: keyBox)
            .Build();
        var rule = new DateRule("Application date", "application date");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Equal(ValidationStatus.Missing, result.Status);
        Assert.Equal(keyBox, result.BoundingBox);
    }

    [Fact]
    public async Task Reports_No_Region_When_The_Date_Field_Was_Never_Found()
    {
        var document = new NormalizedDocumentBuilder(DocumentType.PermitApplication)
            .WithTextField("owner name", "Trenton Okafor")
            .Build();
        var rule = new DateRule("Application date", "application date");

        var result = await rule.ValidateAsync(NormalizedDocumentBuilder.ContextFor(document), CancellationToken.None);

        Assert.Null(result.BoundingBox);
    }
}
