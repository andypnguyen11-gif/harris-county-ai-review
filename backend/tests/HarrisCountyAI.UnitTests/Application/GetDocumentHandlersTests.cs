using HarrisCountyAI.Application.Documents.GetDocument;
using HarrisCountyAI.Application.Documents.GetDocuments;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Application;

public class GetDocumentHandlersTests
{
    private readonly FakeCaseRepository _caseRepository = new();
    private readonly FakeDocumentRepository _documentRepository = new();

    private async Task<Case> AddCaseAsync()
    {
        var @case = Case.Create("HC-2026-0001", "Owner", WorkflowType.FloodplainDevelopmentPermit);
        await _caseRepository.AddAsync(@case);
        return @case;
    }

    private async Task<Document> AddDocumentAsync(Guid caseId, string fileName = "file.pdf")
    {
        var document = Document.Create(caseId, fileName, $"{caseId}/blob_{fileName}", DocumentType.SupportingDocument);
        await _documentRepository.AddAsync(document);
        return document;
    }

    [Fact]
    public async Task GetDocuments_Returns_Dtos_For_Existing_Case()
    {
        var @case = await AddCaseAsync();
        await AddDocumentAsync(@case.Id, "a.pdf");
        await AddDocumentAsync(@case.Id, "b.pdf");
        await AddDocumentAsync(Guid.NewGuid(), "other-case.pdf");

        var dtos = await new GetDocumentsHandler(_caseRepository, _documentRepository).HandleAsync(@case.Id);

        Assert.NotNull(dtos);
        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d => Assert.Equal(@case.Id, d.CaseId));
    }

    [Fact]
    public async Task GetDocuments_Returns_Empty_List_For_Case_Without_Documents()
    {
        var @case = await AddCaseAsync();

        var dtos = await new GetDocumentsHandler(_caseRepository, _documentRepository).HandleAsync(@case.Id);

        Assert.NotNull(dtos);
        Assert.Empty(dtos);
    }

    [Fact]
    public async Task GetDocuments_Returns_Null_For_Unknown_Case()
    {
        var dtos = await new GetDocumentsHandler(_caseRepository, _documentRepository).HandleAsync(Guid.NewGuid());

        Assert.Null(dtos);
    }

    [Fact]
    public async Task GetDocument_Returns_Dto_For_Existing_Document()
    {
        var @case = await AddCaseAsync();
        var document = await AddDocumentAsync(@case.Id);

        var dto = await new GetDocumentHandler(_documentRepository).HandleAsync(@case.Id, document.Id);

        Assert.NotNull(dto);
        Assert.Equal(document.Id, dto.Id);
        Assert.Equal("file.pdf", dto.FileName);
    }

    [Fact]
    public async Task GetDocument_Returns_Null_For_Unknown_Document()
    {
        var @case = await AddCaseAsync();

        var dto = await new GetDocumentHandler(_documentRepository).HandleAsync(@case.Id, Guid.NewGuid());

        Assert.Null(dto);
    }

    [Fact]
    public async Task GetDocument_Returns_Null_When_Document_Belongs_To_Another_Case()
    {
        var @case = await AddCaseAsync();
        var foreignDocument = await AddDocumentAsync(Guid.NewGuid());

        var dto = await new GetDocumentHandler(_documentRepository).HandleAsync(@case.Id, foreignDocument.Id);

        Assert.Null(dto);
    }
}
