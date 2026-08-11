using HarrisCountyAI.Application.Search.Chunking;
using HarrisCountyAI.Application.Search.Indexing;

namespace HarrisCountyAI.UnitTests.Search;

public class IndexableChunkTests
{
    [Fact]
    public void FromChunk_Copies_Chunk_Fields_And_Metadata()
    {
        var documentId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var chunk = new DocumentChunk
        {
            ChunkId = $"{documentId:N}-0007",
            DocumentId = documentId,
            Sequence = 7,
            Text = "Provide two copies of the drainage plan.",
            Title = "Drainage Criteria Manual",
            Section = "4.3 Submittals",
            PageNumber = 12,
        };
        var embedding = new float[1536];
        var effectiveDate = new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var indexable = IndexableChunk.FromChunk(
            chunk,
            embedding,
            IndexSourceTypes.CaseDocument,
            title: "Drainage Criteria Manual",
            department: "Engineering",
            permitType: "Floodplain",
            documentType: "Manual",
            effectiveDate: effectiveDate,
            sourceUrl: "https://example.harriscountytx.gov/manual.pdf",
            caseId: caseId);

        Assert.Equal(chunk.ChunkId, indexable.ChunkId);
        Assert.Equal(documentId, indexable.DocumentId);
        Assert.Equal(7, indexable.Sequence);
        Assert.Equal(chunk.Text, indexable.Text);
        Assert.Equal(chunk.Section, indexable.Section);
        Assert.Equal(chunk.PageNumber, indexable.PageNumber);
        Assert.Equal(IndexSourceTypes.CaseDocument, indexable.SourceType);
        Assert.Equal("Drainage Criteria Manual", indexable.Title);
        Assert.Equal("Engineering", indexable.Department);
        Assert.Equal("Floodplain", indexable.PermitType);
        Assert.Equal("Manual", indexable.DocumentType);
        Assert.Equal(effectiveDate, indexable.EffectiveDate);
        Assert.Equal("https://example.harriscountytx.gov/manual.pdf", indexable.SourceUrl);
        Assert.Equal(caseId, indexable.CaseId);
        Assert.Same(embedding, indexable.Embedding);
    }

    [Fact]
    public void FromChunk_Defaults_Optional_Metadata_To_Null()
    {
        var chunk = new DocumentChunk
        {
            ChunkId = $"{Guid.NewGuid():N}-0000",
            DocumentId = Guid.NewGuid(),
            Sequence = 0,
            Text = "text",
        };

        var indexable = IndexableChunk.FromChunk(
            chunk, new float[1536], IndexSourceTypes.KnowledgeBase, title: "Some Title");

        Assert.Null(indexable.Department);
        Assert.Null(indexable.PermitType);
        Assert.Null(indexable.DocumentType);
        Assert.Null(indexable.EffectiveDate);
        Assert.Null(indexable.SourceUrl);
        Assert.Null(indexable.CaseId);
    }

    [Theory]
    [InlineData("KnowledgeBase", true)]
    [InlineData("CaseDocument", true)]
    [InlineData("knowledgebase", false)]
    [InlineData("Corpus", false)]
    [InlineData("", false)]
    public void IndexSourceTypes_Recognizes_Only_The_Two_Known_Values(string value, bool expected)
    {
        Assert.Equal(expected, IndexSourceTypes.IsValid(value));
    }
}
