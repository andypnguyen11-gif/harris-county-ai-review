using System.Text.RegularExpressions;
using HarrisCountyAI.Application.Search.Chunking;

namespace HarrisCountyAI.UnitTests.Search.Chunking;

/// <summary>
/// Runs the chunker against the extracted text of a real Harris County
/// reference document (FEMA MT-EZ instructions) to confirm the heuristics
/// produce sane output on production-shaped input.
/// </summary>
public class RealCorpusChunkingTests
{
    private static readonly Regex PageMarkerPattern = new(
        @"^===== PAGE (\d+) =====\s*$", RegexOptions.Multiline);

    private static ChunkingRequest LoadCorpusDocument()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Search", "Chunking", "TestData", "FEMA_MT-EZ_Instructions.txt");
        var raw = File.ReadAllText(path);

        var pages = new List<ChunkingPage>();
        var matches = PageMarkerPattern.Matches(raw);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : raw.Length;
            pages.Add(new ChunkingPage
            {
                PageNumber = int.Parse(matches[i].Groups[1].Value),
                Text = raw[start..end],
            });
        }

        Assert.True(pages.Count > 1, "Expected the corpus document to contain page markers.");

        return new ChunkingRequest
        {
            DocumentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Title = "MT-EZ Application Form Instructions",
            Pages = pages,
        };
    }

    [Fact]
    public void Produces_No_Empty_Chunks()
    {
        var chunks = new StructureAwareChunkingService().ChunkDocument(LoadCorpusDocument());

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Text)));
    }

    [Fact]
    public void Keeps_Every_Chunk_Within_The_Max_Size()
    {
        var options = new ChunkingOptions();
        var chunks = new StructureAwareChunkingService(options).ChunkDocument(LoadCorpusDocument());

        Assert.All(chunks, chunk => Assert.True(
            chunk.Text.Length <= options.MaxChunkSize,
            $"Chunk {chunk.Sequence} is {chunk.Text.Length} characters."));
    }

    [Fact]
    public void Detects_Sections_In_The_Document()
    {
        var chunks = new StructureAwareChunkingService().ChunkDocument(LoadCorpusDocument());

        var sections = chunks
            .Where(chunk => chunk.Section is not null)
            .Select(chunk => chunk.Section)
            .Distinct()
            .ToList();

        Assert.True(sections.Count > 3, "Expected multiple distinct sections to be detected.");
        Assert.Contains(chunks, chunk => chunk.Section?.Contains("General Background Information") == true);
    }

    [Fact]
    public void Attributes_Chunks_Across_The_Document_Pages()
    {
        var chunks = new StructureAwareChunkingService().ChunkDocument(LoadCorpusDocument());

        Assert.All(chunks, chunk => Assert.NotNull(chunk.PageNumber));
        Assert.True(
            chunks.Select(chunk => chunk.PageNumber).Distinct().Count() > 1,
            "Expected chunks attributed to more than one page.");
        Assert.Equal(1, chunks[0].PageNumber);
    }

    [Fact]
    public void Numbers_Chunks_Sequentially_With_Unique_Ids()
    {
        var chunks = new StructureAwareChunkingService().ChunkDocument(LoadCorpusDocument());

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.Sequence));
        Assert.Equal(chunks.Count, chunks.Select(chunk => chunk.ChunkId).Distinct().Count());
        Assert.All(chunks, chunk => Assert.Matches("^aaaaaaaabbbbccccddddeeeeeeeeeeee-\\d{4}$", chunk.ChunkId));
    }
}
