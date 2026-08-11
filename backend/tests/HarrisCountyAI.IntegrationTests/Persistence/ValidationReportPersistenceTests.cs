using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.IntegrationTests.Persistence;

public class ValidationReportPersistenceTests : IClassFixture<SqlServerTestDatabase>
{
    private readonly SqlServerTestDatabase _database;

    public ValidationReportPersistenceTests(SqlServerTestDatabase database)
    {
        _database = database;
    }

    private static ValidationReport CreateReport(Guid? caseId = null) => new()
    {
        Id = Guid.NewGuid(),
        CaseId = caseId ?? Guid.NewGuid(),
        WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
        CreatedAt = DateTime.UtcNow,
        Items =
        [
            new ValidationReportItem
            {
                Id = Guid.NewGuid(),
                Order = 0,
                RuleName = "RequiredFieldRule(Owner name)",
                Requirement = "Owner name",
                ValidationType = ValidationType.Deterministic,
                Status = ValidationStatus.Complete,
                Message = "The field is present.",
                ExtractedValue = "Jane P. Smith",
                DocumentId = Guid.NewGuid(),
                DocumentType = DocumentType.PermitApplication,
                PageNumber = 1,
            },
            new ValidationReportItem
            {
                Id = Guid.NewGuid(),
                Order = 1,
                RuleName = "RequiredDocumentRule(Site plan)",
                Requirement = "Site plan",
                ValidationType = ValidationType.Deterministic,
                Status = ValidationStatus.Missing,
                Message = "No SitePlan document was found in the submission package.",
            },
        ],
    };

    [Fact]
    public async Task ValidationReport_Graph_Round_Trips()
    {
        var report = CreateReport();

        await using (var context = _database.CreateContext())
        {
            context.ValidationReports.Add(report);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var loaded = await context.ValidationReports.SingleAsync(r => r.Id == report.Id);

            Assert.Equal(report.CaseId, loaded.CaseId);
            Assert.Equal(WorkflowType.FloodplainDevelopmentPermit, loaded.WorkflowType);
            Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
            Assert.Equal(2, loaded.Items.Count);

            var ownerName = loaded.Items.Single(i => i.Requirement == "Owner name");
            Assert.Equal(0, ownerName.Order);
            Assert.Equal("RequiredFieldRule(Owner name)", ownerName.RuleName);
            Assert.Equal(ValidationType.Deterministic, ownerName.ValidationType);
            Assert.Equal(ValidationStatus.Complete, ownerName.Status);
            Assert.Equal("The field is present.", ownerName.Message);
            Assert.Equal("Jane P. Smith", ownerName.ExtractedValue);
            Assert.Equal(report.Items[0].DocumentId, ownerName.DocumentId);
            Assert.Equal(DocumentType.PermitApplication, ownerName.DocumentType);
            Assert.Equal(1, ownerName.PageNumber);

            var sitePlan = loaded.Items.Single(i => i.Requirement == "Site plan");
            Assert.Equal(ValidationStatus.Missing, sitePlan.Status);
            Assert.Null(sitePlan.ExtractedValue);
            Assert.Null(sitePlan.DocumentId);
            Assert.Null(sitePlan.DocumentType);
            Assert.Null(sitePlan.PageNumber);
        }
    }

    [Fact]
    public async Task Enums_Are_Stored_As_Strings()
    {
        var report = CreateReport();

        await using (var context = _database.CreateContext())
        {
            context.ValidationReports.Add(report);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var workflowType = await context.Database
                .SqlQuery<string>($"SELECT WorkflowType AS [Value] FROM ValidationReports WHERE Id = {report.Id}")
                .SingleAsync();
            Assert.Equal("FloodplainDevelopmentPermit", workflowType);

            var status = await context.Database
                .SqlQuery<string>($"SELECT Status AS [Value] FROM ValidationReportItems WHERE Requirement = 'Site plan' AND ValidationReportId = {report.Id}")
                .SingleAsync();
            Assert.Equal("Missing", status);
        }
    }

    [Fact]
    public async Task Deleting_Report_Cascades_To_Items()
    {
        var report = CreateReport();

        await using (var context = _database.CreateContext())
        {
            context.ValidationReports.Add(report);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var tracked = await context.ValidationReports.SingleAsync(r => r.Id == report.Id);
            context.ValidationReports.Remove(tracked);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            Assert.False(await context.ValidationReports.AnyAsync(r => r.Id == report.Id));

            var itemCount = await context.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM ValidationReportItems WHERE ValidationReportId = {report.Id}")
                .SingleAsync();
            Assert.Equal(0, itemCount);
        }
    }
}
