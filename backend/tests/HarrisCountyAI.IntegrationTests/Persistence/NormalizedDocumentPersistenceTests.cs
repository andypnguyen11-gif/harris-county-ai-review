using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HarrisCountyAI.IntegrationTests.Persistence;

public class NormalizedDocumentPersistenceTests : IClassFixture<SqlServerTestDatabase>
{
    private readonly SqlServerTestDatabase _database;

    public NormalizedDocumentPersistenceTests(SqlServerTestDatabase database)
    {
        _database = database;
    }

    private static NormalizedDocument CreateNormalizedDocument() => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        CaseId = Guid.NewGuid(),
        DocumentType = DocumentType.PermitApplication,
        RawText = "First page\nSecond page",
        Pages =
        [
            new DocumentPage { Id = Guid.NewGuid(), PageNumber = 1, Text = "First page" },
            new DocumentPage { Id = Guid.NewGuid(), PageNumber = 2, Text = "Second page" },
        ],
        Fields =
        [
            new DocumentField
            {
                Id = Guid.NewGuid(),
                Name = "applicant name",
                Value = "Jane Doe",
                Kind = FieldKind.Text,
                Confidence = 0.92,
                PageNumber = 1,
            },
            new DocumentField
            {
                Id = Guid.NewGuid(),
                Name = "in floodplain",
                Value = ":selected:",
                Kind = FieldKind.Checkbox,
                IsChecked = true,
                Confidence = 0.8,
                PageNumber = 2,
            },
            new DocumentField
            {
                Id = Guid.NewGuid(),
                Name = "applicant signature",
                Kind = FieldKind.Signature,
                IsSigned = false,
            },
        ],
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task NormalizedDocument_Graph_Round_Trips()
    {
        var document = CreateNormalizedDocument();

        await using (var context = _database.CreateContext())
        {
            context.NormalizedDocuments.Add(document);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var loaded = await context.NormalizedDocuments
                .SingleAsync(d => d.Id == document.Id);

            Assert.Equal(document.DocumentId, loaded.DocumentId);
            Assert.Equal(document.CaseId, loaded.CaseId);
            Assert.Equal(DocumentType.PermitApplication, loaded.DocumentType);
            Assert.Equal("First page\nSecond page", loaded.RawText);

            Assert.Equal(2, loaded.Pages.Count);
            var secondPage = loaded.Pages.Single(p => p.PageNumber == 2);
            Assert.Equal("Second page", secondPage.Text);

            Assert.Equal(3, loaded.Fields.Count);

            var name = loaded.Fields.Single(f => f.Name == "applicant name");
            Assert.Equal("Jane Doe", name.Value);
            Assert.Equal(FieldKind.Text, name.Kind);
            Assert.Equal(0.92, name.Confidence);
            Assert.Equal(1, name.PageNumber);
            Assert.Null(name.IsChecked);
            Assert.Null(name.IsSigned);

            var checkbox = loaded.Fields.Single(f => f.Name == "in floodplain");
            Assert.Equal(FieldKind.Checkbox, checkbox.Kind);
            Assert.True(checkbox.IsChecked);

            var signature = loaded.Fields.Single(f => f.Name == "applicant signature");
            Assert.Equal(FieldKind.Signature, signature.Kind);
            Assert.False(signature.IsSigned);
            Assert.Null(signature.Value);
            Assert.Null(signature.Confidence);
            Assert.Null(signature.PageNumber);
        }
    }

    [Fact]
    public async Task FieldKind_Is_Stored_As_String()
    {
        var document = CreateNormalizedDocument();

        await using (var context = _database.CreateContext())
        {
            context.NormalizedDocuments.Add(document);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var kind = await context.Database
                .SqlQuery<string>($"SELECT Kind AS [Value] FROM NormalizedDocumentFields WHERE Name = 'in floodplain' AND NormalizedDocumentId = {document.Id}")
                .SingleAsync();

            Assert.Equal("Checkbox", kind);
        }
    }

    [Fact]
    public async Task Deleting_NormalizedDocument_Cascades_To_Pages_And_Fields()
    {
        var document = CreateNormalizedDocument();

        await using (var context = _database.CreateContext())
        {
            context.NormalizedDocuments.Add(document);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var tracked = await context.NormalizedDocuments.SingleAsync(d => d.Id == document.Id);
            context.NormalizedDocuments.Remove(tracked);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            Assert.False(await context.NormalizedDocuments.AnyAsync(d => d.Id == document.Id));

            var pageCount = await context.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM NormalizedDocumentPages WHERE NormalizedDocumentId = {document.Id}")
                .SingleAsync();
            var fieldCount = await context.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM NormalizedDocumentFields WHERE NormalizedDocumentId = {document.Id}")
                .SingleAsync();

            Assert.Equal(0, pageCount);
            Assert.Equal(0, fieldCount);
        }
    }
}
