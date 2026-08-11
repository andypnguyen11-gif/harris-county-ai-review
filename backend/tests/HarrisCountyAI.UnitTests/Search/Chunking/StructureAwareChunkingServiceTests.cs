using HarrisCountyAI.Application.Search.Chunking;

namespace HarrisCountyAI.UnitTests.Search.Chunking;

public class StructureAwareChunkingServiceTests
{
    private static readonly Guid DocumentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ChunkingRequest Request(params ChunkingPage[] pages) => new()
    {
        DocumentId = DocumentId,
        Title = "Floodplain Management Regulations",
        Pages = pages,
    };

    [Fact]
    public void Keeps_A_Section_That_Fits_As_A_Single_Chunk()
    {
        var service = new StructureAwareChunkingService();
        var text =
            "1. Purpose\n" +
            "These regulations protect life and property from flood hazards.\n" +
            "\n" +
            "They apply throughout the unincorporated areas of Harris County.\n" +
            "\n" +
            "2. Applicability\n" +
            "A permit is required before development begins within any special flood hazard area.";

        var chunks = service.ChunkDocument(Request(new ChunkingPage { PageNumber = 1, Text = text }));

        Assert.Equal(2, chunks.Count);
        Assert.Contains("These regulations protect life", chunks[0].Text);
        Assert.Contains("They apply throughout", chunks[0].Text);
        Assert.Contains("A permit is required", chunks[1].Text);
        Assert.DoesNotContain("A permit is required", chunks[0].Text);
    }

    [Fact]
    public void Detects_Numbered_Headings_As_Section_Labels()
    {
        var service = new StructureAwareChunkingService();
        var text =
            "3.1 Elevation Requirements\n" +
            "The lowest floor must be elevated above the base flood elevation.";

        var chunks = service.ChunkDocument(Request(new ChunkingPage { PageNumber = 4, Text = text }));

        var chunk = Assert.Single(chunks);
        Assert.Equal("3.1 Elevation Requirements", chunk.Section);
        Assert.StartsWith("3.1 Elevation Requirements", chunk.Text);
    }

    [Fact]
    public void Detects_AllCaps_Lines_As_Section_Labels()
    {
        var service = new StructureAwareChunkingService();
        var text =
            "Introductory sentence before any heading appears here.\n" +
            "\n" +
            "GENERAL PROVISIONS\n" +
            "No structure shall be located or altered without full compliance with these provisions.";

        var chunks = service.ChunkDocument(Request(new ChunkingPage { Text = text }));

        Assert.Equal(2, chunks.Count);
        Assert.Null(chunks[0].Section);
        Assert.Equal("GENERAL PROVISIONS", chunks[1].Section);
    }

    [Fact]
    public void Detects_Short_Standalone_Lines_Without_Terminal_Punctuation_As_Headings()
    {
        var service = new StructureAwareChunkingService();
        var text =
            "Some opening paragraph that ends with a period.\n" +
            "\n" +
            "Use of Application Forms\n" +
            "The forms provide requesters with a comprehensive, step-by-step process to follow.";

        var chunks = service.ChunkDocument(Request(new ChunkingPage { Text = text }));

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Use of Application Forms", chunks[1].Section);
    }

    [Fact]
    public void Splits_An_Oversized_Section_At_Paragraph_Boundaries()
    {
        var options = new ChunkingOptions { MaxChunkSize = 200, OverlapSize = 0 };
        var service = new StructureAwareChunkingService(options);
        var paragraphOne = new string('a', 90);
        var paragraphTwo = new string('b', 90);
        var paragraphThree = new string('c', 90);
        var text = $"SECTION ONE\n{paragraphOne}\n\n{paragraphTwo}\n\n{paragraphThree}";

        var chunks = service.ChunkDocument(Request(new ChunkingPage { Text = text }));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= options.MaxChunkSize));
        Assert.All(chunks, chunk => Assert.Equal("SECTION ONE", chunk.Section));
        // Paragraphs are kept whole rather than cut mid-way.
        Assert.Contains(chunks, chunk => chunk.Text.Contains(paragraphOne));
        Assert.Contains(chunks, chunk => chunk.Text.Contains(paragraphThree));
    }

    [Fact]
    public void Adds_Overlap_Only_When_A_Section_Is_Split()
    {
        var options = new ChunkingOptions { MaxChunkSize = 300, OverlapSize = 60 };
        var service = new StructureAwareChunkingService(options);
        var sentences = string.Join(
            " ",
            Enumerable.Range(1, 12).Select(i => $"Sentence number {i} concerns floodplain rule {i}."));
        var text = $"LONG SECTION\n{sentences}\n\nSHORT SECTION\nOne small paragraph.";

        var chunks = service.ChunkDocument(Request(new ChunkingPage { Text = text }));

        var longChunks = chunks.Where(chunk => chunk.Section == "LONG SECTION").ToList();
        Assert.True(longChunks.Count > 1);

        // Continuation chunks repeat the tail of the previous chunk.
        var previousTail = longChunks[0].Text[^20..];
        Assert.Contains(previousTail.Trim(), longChunks[1].Text);

        // The intact section carries no overlap from its neighbor.
        var shortChunk = Assert.Single(chunks, chunk => chunk.Section == "SHORT SECTION");
        Assert.DoesNotContain("Sentence number", shortChunk.Text);
        Assert.Equal($"SHORT SECTION\n\nOne small paragraph.", shortChunk.Text);
    }

    [Fact]
    public void Splits_A_Single_Oversized_Paragraph_At_Sentence_Boundaries()
    {
        var options = new ChunkingOptions { MaxChunkSize = 150, OverlapSize = 0 };
        var service = new StructureAwareChunkingService(options);
        var text = string.Join(
            " ",
            Enumerable.Range(1, 10).Select(i => $"This is complete sentence number {i}."));

        var chunks = service.ChunkDocument(Request(new ChunkingPage { Text = text }));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= options.MaxChunkSize));
        // Splits land after sentence terminators, not mid-sentence.
        Assert.All(chunks, chunk => Assert.EndsWith(".", chunk.Text));
    }

    [Fact]
    public void Respects_Max_Chunk_Size_Even_For_Unbroken_Text()
    {
        var options = new ChunkingOptions { MaxChunkSize = 100, OverlapSize = 20 };
        var service = new StructureAwareChunkingService(options);
        var text = new string('x', 950);

        var chunks = service.ChunkDocument(Request(new ChunkingPage { Text = text }));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= options.MaxChunkSize));
    }

    [Fact]
    public void Attributes_Chunks_To_The_Page_Their_Content_Starts_On()
    {
        var service = new StructureAwareChunkingService();

        var chunks = service.ChunkDocument(Request(
            new ChunkingPage
            {
                PageNumber = 1,
                Text = "1. Purpose\nProtect life and property from flood hazards.",
            },
            new ChunkingPage
            {
                PageNumber = 2,
                Text = "2. Definitions\nBase flood means the flood having a one percent chance of occurring.",
            }));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(1, chunks[0].PageNumber);
        Assert.Equal(2, chunks[1].PageNumber);
    }

    [Fact]
    public void Leaves_Page_Number_Null_When_Pages_Are_Unnumbered()
    {
        var service = new StructureAwareChunkingService();

        var chunks = service.ChunkDocument(
            Request(new ChunkingPage { Text = "A single paragraph without any page metadata." }));

        var chunk = Assert.Single(chunks);
        Assert.Null(chunk.PageNumber);
    }

    [Fact]
    public void Builds_Chunk_Ids_From_Document_Id_And_Sequence()
    {
        var service = new StructureAwareChunkingService();
        var text = "1. One\nFirst section body.\n\n2. Two\nSecond section body.";

        var chunks = service.ChunkDocument(Request(new ChunkingPage { Text = text }));

        Assert.Equal(2, chunks.Count);
        Assert.Equal("11111111222233334444555555555555-0000", chunks[0].ChunkId);
        Assert.Equal("11111111222233334444555555555555-0001", chunks[1].ChunkId);
        Assert.Equal(0, chunks[0].Sequence);
        Assert.Equal(1, chunks[1].Sequence);
        Assert.All(chunks, chunk => Assert.Equal(DocumentId, chunk.DocumentId));
    }

    [Fact]
    public void Propagates_The_Document_Title_To_Every_Chunk()
    {
        var service = new StructureAwareChunkingService();

        var chunks = service.ChunkDocument(
            Request(new ChunkingPage { Text = "1. One\nBody.\n\n2. Two\nBody." }));

        Assert.All(chunks, chunk => Assert.Equal("Floodplain Management Regulations", chunk.Title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t\n  ")]
    public void Returns_No_Chunks_For_Empty_Or_Whitespace_Input(string text)
    {
        var service = new StructureAwareChunkingService();

        var chunks = service.ChunkDocument(Request(new ChunkingPage { Text = text }));

        Assert.Empty(chunks);
    }

    [Fact]
    public void Returns_No_Chunks_When_There_Are_No_Pages()
    {
        var service = new StructureAwareChunkingService();

        var chunks = service.ChunkDocument(Request());

        Assert.Empty(chunks);
    }

    [Fact]
    public void FromText_Wraps_Unpaged_Text_In_A_Single_Page()
    {
        var request = ChunkingRequest.FromText(DocumentId, "Title", "Some text.");

        var page = Assert.Single(request.Pages);
        Assert.Null(page.PageNumber);
        Assert.Equal("Some text.", page.Text);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 100)]
    [InlineData(100, -1)]
    [InlineData(100, 100)]
    [InlineData(100, 150)]
    public void Rejects_Invalid_Options(int maxChunkSize, int overlapSize)
    {
        var options = new ChunkingOptions { MaxChunkSize = maxChunkSize, OverlapSize = overlapSize };

        Assert.Throws<ArgumentOutOfRangeException>(() => new StructureAwareChunkingService(options));
    }
}
