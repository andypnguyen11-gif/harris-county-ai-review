using HarrisCountyAI.Application.Cases.UpdateCase;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Application;

public class UpdateCaseHandlerTests
{
    private readonly FakeCaseRepository _repository = new();
    private readonly UpdateCaseHandler _handler;

    public UpdateCaseHandlerTests()
    {
        _handler = new UpdateCaseHandler(_repository);
    }

    [Fact]
    public async Task Returns_Null_For_Unknown_Case()
    {
        var result = await _handler.HandleAsync(Guid.NewGuid(), new UpdateCaseCommand("New Name", null));

        Assert.Null(result);
        Assert.Equal(0, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Updates_Name_Only()
    {
        var @case = Case.Create("HC-2026-0001", "Original", WorkflowType.FloodplainDevelopmentPermit);
        await _repository.AddAsync(@case);

        var result = await _handler.HandleAsync(@case.Id, new UpdateCaseCommand("Renamed", null));

        Assert.NotNull(result);
        Assert.Equal("Renamed", result.Name);
        Assert.Equal(CaseStatus.New, result.Status);
        Assert.Equal(1, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Updates_Status_Only()
    {
        var @case = Case.Create("HC-2026-0001", "Original", WorkflowType.FloodplainDevelopmentPermit);
        await _repository.AddAsync(@case);

        var result = await _handler.HandleAsync(@case.Id, new UpdateCaseCommand(null, CaseStatus.InReview));

        Assert.NotNull(result);
        Assert.Equal("Original", result.Name);
        Assert.Equal(CaseStatus.InReview, result.Status);
    }

    [Fact]
    public async Task Updates_Name_And_Status_Together()
    {
        var @case = Case.Create("HC-2026-0001", "Original", WorkflowType.FloodplainDevelopmentPermit);
        await _repository.AddAsync(@case);

        var result = await _handler.HandleAsync(@case.Id, new UpdateCaseCommand("Renamed", CaseStatus.Completed));

        Assert.NotNull(result);
        Assert.Equal("Renamed", result.Name);
        Assert.Equal(CaseStatus.Completed, result.Status);
    }
}
