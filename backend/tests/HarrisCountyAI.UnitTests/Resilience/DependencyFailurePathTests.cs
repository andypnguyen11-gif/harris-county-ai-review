using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Application.Documents.Normalization;
using HarrisCountyAI.Application.QuestionAnswering;
using HarrisCountyAI.Application.Search.Reranking;
using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Infrastructure.Azure.Search;
using HarrisCountyAI.UnitTests.Common.AI;
using HarrisCountyAI.UnitTests.Documents.Extraction;
using HarrisCountyAI.UnitTests.QuestionAnswering;
using HarrisCountyAI.UnitTests.Search.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Resilience;

/// <summary>
/// What each dependency going down actually does to the feature that needs it.
/// </summary>
/// <remarks>
/// Two different answers are correct here, and the difference is deliberate.
/// A dependency whose failure leaves the result unverifiable — retrieval,
/// extraction, storage — fails the request, so the reviewer is told rather
/// than shown a partial answer they might trust. A dependency that only
/// improves an already-correct result — semantic reranking, search indexing of
/// an already-persisted document — fails open, because degrading quietly beats
/// blocking work that can proceed.
/// </remarks>
public class DependencyFailurePathTests
{
    private static readonly ExternalServiceUnavailableException SearchDown =
        new(ExternalServiceNames.Search, "search returned 503", statusCode: 503);

    // ---- Retrieval: the answer would be unverifiable, so the request fails ----

    [Fact]
    public async Task Search_Being_Down_Fails_Retrieval_Rather_Than_Returning_Nothing()
    {
        // Returning an empty list here would be read downstream as "the corpus
        // has nothing on this", which is a different and much worse answer.
        var gateway = new FakeSearchQueryGateway { ExceptionToThrow = SearchDown };
        var service = new AzureRetrievalService(gateway, new FakeEmbeddingService());

        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => service.RetrieveAsync(new RetrievalRequest { Query = "setback requirements" }));

        Assert.Equal(ExternalServiceNames.Search, exception.ServiceName);
    }

    [Fact]
    public async Task Search_Being_Down_Fails_Question_Answering_Rather_Than_Answering_Ungrounded()
    {
        var retrieval = new ThrowingRetrievalService(SearchDown);
        var service = new QuestionAnsweringService(retrieval, new FakeLanguageModelService());

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => service.AnswerAsync(new QuestionRequest { Question = "What is required?" }));
    }

    // ---- Reranking: only reorders an already-correct result, so it fails open ----

    [Fact]
    public async Task Reranking_Being_Down_Leaves_Retrieval_Working()
    {
        var gateway = new FakeSearchQueryGateway { ExceptionToThrow = SearchDown };
        var service = new AzureSemanticRerankingService(
            gateway,
            Options.Create(new RerankingOptions { Enabled = true }));

        var candidates = new[] { Chunk("a"), Chunk("b") };
        var result = await service.RerankAsync(new RerankingRequest
        {
            Query = "setback requirements",
            Candidates = candidates,
            TopN = 2,
        });

        Assert.Equal(["a", "b"], result.Select(chunk => chunk.ChunkId));
    }

    // ---- The model: the pipeline degrades to an honest "not answered" ----

    [Fact]
    public async Task The_Model_Being_Down_Yields_A_Failed_Outcome_Not_An_Exception()
    {
        var retrieval = new FakeRetrievalService { ChunksToReturn = [Chunk("a")] };
        var model = new FakeLanguageModelService().EnqueueException(
            new ExternalServiceUnavailableException(
                ExternalServiceNames.LanguageModel, "model returned 503", statusCode: 503));
        var service = new QuestionAnsweringService(retrieval, model);

        var response = await service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        Assert.Equal(QuestionAnswerOutcome.Failed, response.Outcome);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public async Task Unusable_Model_Output_Yields_A_Failed_Outcome_Not_An_Invented_Answer()
    {
        var retrieval = new FakeRetrievalService { ChunksToReturn = [Chunk("a")] };
        var model = new FakeLanguageModelService().EnqueueException(
            new MalformedModelResponseException("the model returned no content"));
        var service = new QuestionAnsweringService(retrieval, model);

        var response = await service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        Assert.Equal(QuestionAnswerOutcome.Failed, response.Outcome);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"status":"answered"}""")]
    [InlineData("""{"answer":"yes"}""")]
    [InlineData("""{"status":"maybe","answer":"yes","citations":[1]}""")]
    public async Task Model_Output_That_Breaks_The_Contract_Never_Becomes_An_Answer(string content)
    {
        var retrieval = new FakeRetrievalService { ChunksToReturn = [Chunk("a")] };
        var model = new FakeLanguageModelService().EnqueueContent(content);
        var service = new QuestionAnsweringService(retrieval, model);

        var response = await service.AnswerAsync(new QuestionRequest { Question = "What is required?" });

        Assert.NotEqual(QuestionAnswerOutcome.Answered, response.Outcome);
        Assert.Empty(response.Citations);
    }

    // ---- Extraction and storage: the document is marked failed, never half-processed ----

    [Fact]
    public async Task Document_Intelligence_Being_Down_Marks_The_Document_Failed()
    {
        var harness = new ProcessingHarness();
        harness.Extraction.ExtractException = new ExternalServiceUnavailableException(
            ExternalServiceNames.DocumentIntelligence, "extraction returned 503", statusCode: 503);

        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => harness.Service.ProcessAsync(harness.Document.Id));

        Assert.Equal(ExternalServiceNames.DocumentIntelligence, exception.ServiceName);
        Assert.Equal(DocumentProcessingStatus.Failed, harness.Document.ProcessingStatus);
        // Nothing half-written: no normalized snapshot was persisted.
        Assert.Empty(harness.NormalizedRepository.Added);
    }

    [Fact]
    public async Task Document_Intelligence_Timing_Out_Marks_The_Document_Failed()
    {
        var harness = new ProcessingHarness();
        harness.Extraction.ExtractException = new ExternalServiceTimeoutException(
            ExternalServiceNames.DocumentIntelligence, "analysis timed out after 120s");

        await Assert.ThrowsAsync<ExternalServiceTimeoutException>(
            () => harness.Service.ProcessAsync(harness.Document.Id));

        Assert.Equal(DocumentProcessingStatus.Failed, harness.Document.ProcessingStatus);
    }

    [Fact]
    public async Task Blob_Storage_Being_Down_Marks_The_Document_Failed()
    {
        var harness = new ProcessingHarness();
        harness.Storage.DownloadException = new ExternalServiceUnavailableException(
            ExternalServiceNames.DocumentStorage, "storage returned 503", statusCode: 503);

        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => harness.Service.ProcessAsync(harness.Document.Id));

        Assert.Equal(ExternalServiceNames.DocumentStorage, exception.ServiceName);
        Assert.Equal(DocumentProcessingStatus.Failed, harness.Document.ProcessingStatus);
    }

    [Fact]
    public async Task A_Missing_Stored_File_Marks_The_Document_Failed_Too()
    {
        var harness = new ProcessingHarness();
        harness.Storage.DownloadException = new FileNotFoundException("gone", "cases/x/application.pdf");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => harness.Service.ProcessAsync(harness.Document.Id));

        Assert.Equal(DocumentProcessingStatus.Failed, harness.Document.ProcessingStatus);
    }

    private static RetrievedChunk Chunk(string chunkId) => new()
    {
        ChunkId = chunkId,
        DocumentId = Guid.NewGuid(),
        Text = "A completed application form is required.",
        Title = "Floodplain Regulations",
        Score = 0.9,
    };

    /// <summary>A document ready to process, with every dependency faked.</summary>
    private sealed class ProcessingHarness
    {
        public ProcessingHarness()
        {
            var permitCase = Case.Create("HC-2026-0042", "Failure Case", WorkflowType.FloodplainDevelopmentPermit);
            Document = permitCase.AddDocument(
                "application.pdf",
                $"cases/{permitCase.Id}/application.pdf",
                DocumentType.PermitApplication);
            Document.SetProcessingStatus(DocumentProcessingStatus.Uploaded);

            Repository.Add(Document);
            Storage.AddBlob(DocumentStorageContainer.CaseDocuments, Document.BlobPath, [1, 2, 3]);

            Service = new DocumentProcessingService(
                Repository,
                Storage,
                Extraction,
                new DocumentNormalizationService(),
                NormalizedRepository,
                NullLogger<DocumentProcessingService>.Instance);
        }

        public FakeDocumentRepository Repository { get; } = new();

        public FakeDocumentStorageService Storage { get; } = new();

        public FakeDocumentExtractionService Extraction { get; } = new();

        public FakeNormalizedDocumentRepository NormalizedRepository { get; } = new();

        public DocumentProcessingService Service { get; }

        public Document Document { get; }
    }

    /// <summary>Retrieval that always fails, standing in for a search outage.</summary>
    private sealed class ThrowingRetrievalService(Exception exception) : IRetrievalService
    {
        public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
            RetrievalRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromException<IReadOnlyList<RetrievedChunk>>(exception);
    }
}
