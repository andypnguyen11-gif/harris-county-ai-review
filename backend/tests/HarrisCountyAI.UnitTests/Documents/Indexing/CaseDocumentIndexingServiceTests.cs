using HarrisCountyAI.Application.Documents.Indexing;
using HarrisCountyAI.Application.Search.Chunking;
using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.UnitTests.Documents.Extraction;
using HarrisCountyAI.UnitTests.KnowledgeBase.Ingestion;

namespace HarrisCountyAI.UnitTests.Documents.Indexing;

public class CaseDocumentIndexingServiceTests
{
    private readonly FakeDocumentRepository _documents = new();
    private readonly FakeNormalizedDocumentRepository _normalized = new();
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly FakeDocumentIndexService _index = new();
    private readonly CaseDocumentIndexingService _service;

    public CaseDocumentIndexingServiceTests()
    {
        _service = new CaseDocumentIndexingService(
            _documents,
            _normalized,
            new StructureAwareChunkingService(),
            _embeddings,
            _index);
    }

    private Document AddDocument(
        Guid? caseId = null,
        string fileName = "site-plan.pdf",
        DocumentType documentType = DocumentType.SitePlan)
    {
        var document = Document.Create(
            caseId ?? Guid.NewGuid(), fileName, $"cases/x/{fileName}", documentType);
        _documents.Add(document);
        return document;
    }

    private NormalizedDocument AddSnapshot(Document document, params string[] pageTexts)
    {
        var snapshot = new NormalizedDocument
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            CaseId = document.CaseId,
            DocumentType = document.DocumentType,
            RawText = string.Join("\n", pageTexts),
            Pages = pageTexts
                .Select((text, index) => new DocumentPage { PageNumber = index + 1, Text = text })
                .ToList(),
            CreatedAt = DateTime.UtcNow,
        };
        _normalized.Added.Add(snapshot);
        return snapshot;
    }

    [Fact]
    public async Task Indexes_Every_Chunk_As_CaseDocument_With_The_Owning_CaseId()
    {
        var document = AddDocument();
        AddSnapshot(document, "The proposed site plan shows a detached garage.", "A drainage swale runs north.");

        var result = await _service.IndexAsync(document.Id);

        Assert.NotNull(result);
        Assert.Equal(document.Id, result.DocumentId);
        Assert.Equal(document.CaseId, result.CaseId);
        Assert.True(result.ChunkCount > 0);

        var batch = Assert.Single(_index.IndexedBatches);
        Assert.Equal(result.ChunkCount, batch.Count);
        Assert.All(batch, chunk =>
        {
            Assert.Equal(IndexSourceTypes.CaseDocument, chunk.SourceType);
            Assert.Equal(document.CaseId, chunk.CaseId);
            Assert.Equal(document.Id, chunk.DocumentId);
        });
    }

    [Fact]
    public async Task Never_Tags_A_Case_Chunk_As_KnowledgeBase()
    {
        var document = AddDocument();
        AddSnapshot(document, "Elevation certificate attached for the main structure.");

        await _service.IndexAsync(document.Id);

        Assert.All(
            _index.IndexedBatches.SelectMany(batch => batch),
            chunk =>
            {
                Assert.NotEqual(IndexSourceTypes.KnowledgeBase, chunk.SourceType);
                Assert.NotNull(chunk.CaseId);
                Assert.NotEqual(Guid.Empty, chunk.CaseId);
            });
    }

    [Fact]
    public async Task Carries_File_Name_Document_Type_And_Page_Numbers_Onto_The_Chunks()
    {
        var document = AddDocument(fileName: "drainage-plan.pdf", documentType: DocumentType.DrainagePlan);
        AddSnapshot(document, "Culvert sizing calculations for the proposed driveway.");

        await _service.IndexAsync(document.Id);

        var chunk = Assert.Single(Assert.Single(_index.IndexedBatches));
        Assert.Equal("drainage-plan.pdf", chunk.Title);
        Assert.Equal(nameof(DocumentType.DrainagePlan), chunk.DocumentType);
        Assert.Equal(1, chunk.PageNumber);
        Assert.Null(chunk.Department);
        Assert.Null(chunk.SourceUrl);
    }

    [Fact]
    public async Task Deletes_Existing_Chunks_Before_Indexing_New_Ones()
    {
        var document = AddDocument();
        AddSnapshot(document, "First revision of the site plan.");

        await _service.IndexAsync(document.Id);

        Assert.Equal(
            ["EnsureIndex", $"Delete:{document.Id}", "Index:1"],
            _index.Operations);
    }

    [Fact]
    public async Task Returns_Null_For_An_Unknown_Document()
    {
        var result = await _service.IndexAsync(Guid.NewGuid());

        Assert.Null(result);
        Assert.Empty(_index.Operations);
    }

    [Fact]
    public async Task Returns_Null_When_The_Document_Has_No_Normalized_Snapshot()
    {
        var document = AddDocument();

        var result = await _service.IndexAsync(document.Id);

        Assert.Null(result);
        Assert.Empty(_index.Operations);
    }

    [Fact]
    public async Task A_Snapshot_Without_Text_Removes_Existing_Chunks_And_Indexes_Nothing()
    {
        var document = AddDocument();
        AddSnapshot(document, "   ");

        var result = await _service.IndexAsync(document.Id);

        Assert.NotNull(result);
        Assert.Equal(0, result.ChunkCount);
        Assert.Equal(["EnsureIndex", $"Delete:{document.Id}"], _index.Operations);
        Assert.Empty(_embeddings.Requests);
    }

    [Fact]
    public async Task Falls_Back_To_Raw_Text_When_The_Snapshot_Has_No_Pages()
    {
        var document = AddDocument();
        var snapshot = AddSnapshot(document);
        snapshot.RawText = "Unpaged affidavit text.";

        var result = await _service.IndexAsync(document.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result.ChunkCount);
        var chunk = Assert.Single(Assert.Single(_index.IndexedBatches));
        Assert.Equal("Unpaged affidavit text.", chunk.Text);
        Assert.Null(chunk.PageNumber);
    }

    [Fact]
    public async Task An_Embedding_Count_Mismatch_Throws_Without_Indexing()
    {
        var document = AddDocument();
        AddSnapshot(document, "Some page text.");
        _embeddings.ResultCountOverride = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.IndexAsync(document.Id));

        Assert.Empty(_index.IndexedBatches);
    }

    [Fact]
    public async Task Matches_Embeddings_To_Chunks_By_Input_Index()
    {
        var document = AddDocument();
        AddSnapshot(document, "1. Overview\nFirst section text here.", "2. Details\nSecond section text here.");
        _embeddings.ReverseResultOrder = true;

        await _service.IndexAsync(document.Id);

        var batch = Assert.Single(_index.IndexedBatches);
        for (var index = 0; index < batch.Count; index++)
        {
            Assert.Equal(index + 1, batch[index].Embedding[0]);
        }
    }

    [Fact]
    public async Task RemoveAsync_Deletes_The_Documents_Chunks()
    {
        var documentId = Guid.NewGuid();

        await _service.RemoveAsync(documentId);

        Assert.Equal([$"Delete:{documentId}"], _index.Operations);
    }
}
