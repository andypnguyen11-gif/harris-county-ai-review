using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.IntegrationTests.Persistence;

public class KnowledgeDocumentPersistenceTests : IClassFixture<SqlServerTestDatabase>
{
    private readonly SqlServerTestDatabase _database;

    public KnowledgeDocumentPersistenceTests(SqlServerTestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task KnowledgeDocument_Round_Trips_Through_Repository()
    {
        var id = Guid.NewGuid();
        var document = KnowledgeDocument.Create(
            id,
            "Floodplain Management Regulations",
            "regulations.pdf",
            $"knowledge/{id:D}_regulations.pdf",
            "Engineering",
            "Regulation",
            "FloodplainDevelopment",
            "2026.1",
            new DateOnly(2026, 1, 15),
            "https://www.harriscountytx.gov/regulations.pdf");

        await using (var context = _database.CreateContext())
        {
            var repository = new KnowledgeDocumentRepository(context);
            await repository.AddAsync(document);
            await repository.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var repository = new KnowledgeDocumentRepository(context);
            var loaded = await repository.GetByIdAsync(id);

            Assert.NotNull(loaded);
            Assert.Equal("Floodplain Management Regulations", loaded.Title);
            Assert.Equal("regulations.pdf", loaded.FileName);
            Assert.Equal($"knowledge/{id:D}_regulations.pdf", loaded.BlobPath);
            Assert.Equal("Engineering", loaded.Department);
            Assert.Equal("Regulation", loaded.DocumentType);
            Assert.Equal("FloodplainDevelopment", loaded.PermitType);
            Assert.Equal("2026.1", loaded.Version);
            Assert.Equal(new DateOnly(2026, 1, 15), loaded.EffectiveDate);
            Assert.Equal("https://www.harriscountytx.gov/regulations.pdf", loaded.SourceUrl);
            Assert.Equal(KnowledgeDocumentIngestionStatus.Uploaded, loaded.IngestionStatus);
            Assert.Null(loaded.IngestionDate);
        }
    }

    [Fact]
    public async Task GetById_Returns_Null_For_Unknown_Id()
    {
        await using var context = _database.CreateContext();
        var repository = new KnowledgeDocumentRepository(context);

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAll_Excludes_Deactivated_Unless_Requested()
    {
        var active = CreateDocument("Active Checklist");
        var deactivated = CreateDocument("Retired Checklist");
        deactivated.Deactivate();

        await using (var context = _database.CreateContext())
        {
            var repository = new KnowledgeDocumentRepository(context);
            await repository.AddAsync(active);
            await repository.AddAsync(deactivated);
            await repository.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var repository = new KnowledgeDocumentRepository(context);

            var defaultList = await repository.GetAllAsync();
            Assert.Contains(defaultList, d => d.Id == active.Id);
            Assert.DoesNotContain(defaultList, d => d.Id == deactivated.Id);

            var fullList = await repository.GetAllAsync(includeDeactivated: true);
            Assert.Contains(fullList, d => d.Id == active.Id);
            Assert.Contains(fullList, d => d.Id == deactivated.Id);
        }
    }

    [Fact]
    public async Task Ingestion_State_Changes_Persist()
    {
        var document = CreateDocument("Stateful Document");

        await using (var context = _database.CreateContext())
        {
            var repository = new KnowledgeDocumentRepository(context);
            await repository.AddAsync(document);
            await repository.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var repository = new KnowledgeDocumentRepository(context);
            var loaded = await repository.GetByIdAsync(document.Id);

            loaded!.MarkProcessing();
            loaded.MarkIngested();
            await repository.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var repository = new KnowledgeDocumentRepository(context);
            var loaded = await repository.GetByIdAsync(document.Id);

            Assert.Equal(KnowledgeDocumentIngestionStatus.Ingested, loaded!.IngestionStatus);
            Assert.NotNull(loaded.IngestionDate);
            Assert.Equal(DateTimeKind.Utc, loaded.IngestionDate.Value.Kind);
        }
    }

    [Fact]
    public async Task IngestionStatus_Is_Stored_As_String()
    {
        var document = CreateDocument("String Status Document");

        await using (var context = _database.CreateContext())
        {
            context.KnowledgeDocuments.Add(document);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var status = await context.Database
                .SqlQuery<string>($"SELECT IngestionStatus AS [Value] FROM KnowledgeDocuments WHERE Id = {document.Id}")
                .SingleAsync();

            Assert.Equal("Uploaded", status);
        }
    }

    private static KnowledgeDocument CreateDocument(string title)
    {
        var id = Guid.NewGuid();
        return KnowledgeDocument.Create(
            id,
            title,
            "checklist.pdf",
            $"knowledge/{id:D}_checklist.pdf",
            "Permits",
            "Checklist",
            "FloodplainDevelopment");
    }
}
