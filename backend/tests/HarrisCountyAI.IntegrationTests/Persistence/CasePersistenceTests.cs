using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.IntegrationTests.Persistence;

public class CasePersistenceTests : IClassFixture<SqlServerTestDatabase>
{
    private readonly SqlServerTestDatabase _database;

    public CasePersistenceTests(SqlServerTestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task Case_With_Documents_Round_Trips()
    {
        var @case = Case.Create($"HC-2026-{Random.Shared.Next(1000, 9999)}", "Round Trip Case", WorkflowType.FloodplainDevelopmentPermit);
        @case.SetStatus(CaseStatus.Processing);
        @case.AddDocument("application.pdf", $"cases/{@case.Id}/application.pdf", DocumentType.PermitApplication);
        @case.AddDocument("site-plan.pdf", $"cases/{@case.Id}/site-plan.pdf", DocumentType.SitePlan);

        await using (var context = _database.CreateContext())
        {
            context.Cases.Add(@case);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var loaded = await context.Cases
                .Include(c => c.Documents)
                .SingleAsync(c => c.Id == @case.Id);

            Assert.Equal(@case.CaseNumber, loaded.CaseNumber);
            Assert.Equal("Round Trip Case", loaded.Name);
            Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, loaded.WorkflowType);
            Assert.Equal(CaseStatus.Processing, loaded.Status);
            Assert.Equal(2, loaded.Documents.Count);

            var sitePlan = loaded.Documents.Single(d => d.DocumentType == DocumentType.SitePlan);
            Assert.Equal("site-plan.pdf", sitePlan.FileName);
            Assert.Equal($"cases/{@case.Id}/site-plan.pdf", sitePlan.BlobPath);
            Assert.Equal(DocumentProcessingStatus.Pending, sitePlan.ProcessingStatus);
        }
    }

    [Fact]
    public async Task Enums_Are_Stored_As_Strings()
    {
        var @case = Case.Create($"HC-2026-{Random.Shared.Next(1000, 9999)}", "Enum Storage Case", WorkflowType.FloodplainDevelopmentPermit);
        @case.SetStatus(CaseStatus.ReadyForReview);

        await using (var context = _database.CreateContext())
        {
            context.Cases.Add(@case);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var status = await context.Database
                .SqlQuery<string>($"SELECT Status AS [Value] FROM Cases WHERE Id = {@case.Id}")
                .SingleAsync();

            Assert.Equal("ReadyForReview", status);
        }
    }

    [Fact]
    public async Task Deleting_Case_Cascades_To_Documents()
    {
        var @case = Case.Create($"HC-2026-{Random.Shared.Next(1000, 9999)}", "Cascade Case", WorkflowType.FloodplainDevelopmentPermit);
        @case.AddDocument("affidavit.pdf", $"cases/{@case.Id}/affidavit.pdf", DocumentType.Affidavit);

        await using (var context = _database.CreateContext())
        {
            context.Cases.Add(@case);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var tracked = await context.Cases.SingleAsync(c => c.Id == @case.Id);
            context.Cases.Remove(tracked);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            Assert.False(await context.Cases.AnyAsync(c => c.Id == @case.Id));
            Assert.False(await context.Documents.AnyAsync(d => d.CaseId == @case.Id));
        }
    }

    [Fact]
    public async Task Duplicate_CaseNumber_Is_Rejected()
    {
        var caseNumber = $"HC-2026-{Random.Shared.Next(1000, 9999)}";
        var first = Case.Create(caseNumber, "First", WorkflowType.FloodplainDevelopmentPermit);
        var second = Case.Create(caseNumber, "Second", WorkflowType.FloodplainDevelopmentPermit);

        await using (var context = _database.CreateContext())
        {
            context.Cases.Add(first);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            context.Cases.Add(second);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }
}
