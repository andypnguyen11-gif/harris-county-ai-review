using HarrisCountyAI.Application.Cases.CreateCase;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Application;

public class CreateCaseHandlerTests
{
    private readonly FakeCaseRepository _repository = new();
    private readonly CreateCaseHandler _handler;

    public CreateCaseHandlerTests()
    {
        _handler = new CreateCaseHandler(_repository);
    }

    [Fact]
    public async Task Creates_Case_With_Generated_Case_Number()
    {
        var dto = await _handler.HandleAsync(new CreateCaseCommand("First Case", WorkflowType.FloodplainDevelopmentPermit));

        Assert.Equal($"HC-{DateTime.UtcNow.Year}-0001", dto.CaseNumber);
        Assert.Equal("First Case", dto.Name);
        Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, dto.WorkflowType);
        Assert.Equal(CaseStatus.New, dto.Status);
        Assert.Single(_repository.Cases);
        Assert.Equal(1, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Case_Numbers_Increment_Sequentially()
    {
        var first = await _handler.HandleAsync(new CreateCaseCommand("One", WorkflowType.FloodplainDevelopmentPermit));
        var second = await _handler.HandleAsync(new CreateCaseCommand("Two", WorkflowType.FloodplainDevelopmentPermit));
        var third = await _handler.HandleAsync(new CreateCaseCommand("Three", WorkflowType.FloodplainDevelopmentPermit));

        var year = DateTime.UtcNow.Year;
        Assert.Equal($"HC-{year}-0001", first.CaseNumber);
        Assert.Equal($"HC-{year}-0002", second.CaseNumber);
        Assert.Equal($"HC-{year}-0003", third.CaseNumber);
    }

    [Fact]
    public async Task Invalid_Name_Propagates_Domain_Validation()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(new CreateCaseCommand("   ", WorkflowType.FloodplainDevelopmentPermit)));

        Assert.Empty(_repository.Cases);
    }
}
