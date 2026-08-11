using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Domain;

public class CaseTests
{
    [Fact]
    public void Create_Sets_Initial_State()
    {
        var before = DateTime.UtcNow;

        var @case = Case.Create("HC-2026-0001", "Smith Floodplain Permit", WorkflowType.FloodplainDevelopmentPermit);

        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, @case.Id);
        Assert.Equal("HC-2026-0001", @case.CaseNumber);
        Assert.Equal("Smith Floodplain Permit", @case.Name);
        Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, @case.WorkflowType);
        Assert.Equal(CaseStatus.New, @case.Status);
        Assert.InRange(@case.CreatedAt, before, after);
        Assert.Equal(@case.CreatedAt, @case.UpdatedAt);
        Assert.Empty(@case.Documents);
    }

    [Fact]
    public void Create_Trims_CaseNumber_And_Name()
    {
        var @case = Case.Create("  HC-2026-0002  ", "  Trimmed Name  ", WorkflowType.FloodplainDevelopmentPermit);

        Assert.Equal("HC-2026-0002", @case.CaseNumber);
        Assert.Equal("Trimmed Name", @case.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_CaseNumber(string? caseNumber)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Case.Create(caseNumber!, "Name", WorkflowType.FloodplainDevelopmentPermit));

        Assert.Equal("caseNumber", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Missing_Name(string? name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Case.Create("HC-2026-0001", name!, WorkflowType.FloodplainDevelopmentPermit));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Rename_Updates_Name_And_UpdatedAt()
    {
        var @case = Case.Create("HC-2026-0001", "Original", WorkflowType.FloodplainDevelopmentPermit);
        var originalUpdatedAt = @case.UpdatedAt;

        @case.Rename("Renamed");

        Assert.Equal("Renamed", @case.Name);
        Assert.True(@case.UpdatedAt >= originalUpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_Rejects_Missing_Name(string? name)
    {
        var @case = Case.Create("HC-2026-0001", "Original", WorkflowType.FloodplainDevelopmentPermit);

        Assert.Throws<ArgumentException>(() => @case.Rename(name!));
        Assert.Equal("Original", @case.Name);
    }

    [Theory]
    [InlineData(CaseStatus.Processing)]
    [InlineData(CaseStatus.ReadyForReview)]
    [InlineData(CaseStatus.InReview)]
    [InlineData(CaseStatus.Completed)]
    public void SetStatus_Updates_Status(CaseStatus status)
    {
        var @case = Case.Create("HC-2026-0001", "Name", WorkflowType.FloodplainDevelopmentPermit);

        @case.SetStatus(status);

        Assert.Equal(status, @case.Status);
    }

    [Fact]
    public void SetStatus_Rejects_Undefined_Value()
    {
        var @case = Case.Create("HC-2026-0001", "Name", WorkflowType.FloodplainDevelopmentPermit);

        Assert.Throws<ArgumentOutOfRangeException>(() => @case.SetStatus((CaseStatus)999));
        Assert.Equal(CaseStatus.New, @case.Status);
    }

    [Fact]
    public void AddDocument_Links_Document_To_Case()
    {
        var @case = Case.Create("HC-2026-0001", "Name", WorkflowType.FloodplainDevelopmentPermit);

        var document = @case.AddDocument("site-plan.pdf", "cases/1/site-plan.pdf", DocumentType.SitePlan);

        Assert.Single(@case.Documents);
        Assert.Same(document, @case.Documents.Single());
        Assert.Equal(@case.Id, document.CaseId);
    }
}
