using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.UnitTests.Validation;

/// <summary>Fluent in-memory builder for <see cref="NormalizedDocument"/> test data.</summary>
public sealed class NormalizedDocumentBuilder
{
    private readonly NormalizedDocument _document;

    public NormalizedDocumentBuilder(DocumentType documentType, Guid? caseId = null)
    {
        _document = new NormalizedDocument
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            CaseId = caseId ?? Guid.NewGuid(),
            DocumentType = documentType,
            RawText = "",
            CreatedAt = DateTime.UtcNow,
        };
    }

    public NormalizedDocumentBuilder WithTextField(string name, string? value, int? page = 1)
    {
        AddField(name, FieldKind.Text, value: value, page: page);
        return this;
    }

    public NormalizedDocumentBuilder WithDateField(string name, string? value, int? page = 1)
    {
        AddField(name, FieldKind.Date, value: value, page: page);
        return this;
    }

    public NormalizedDocumentBuilder WithNumberField(string name, string? value, int? page = 1)
    {
        AddField(name, FieldKind.Number, value: value, page: page);
        return this;
    }

    public NormalizedDocumentBuilder WithCheckbox(string name, bool isChecked, int? page = 1)
    {
        AddField(name, FieldKind.Checkbox, isChecked: isChecked, page: page);
        return this;
    }

    public NormalizedDocumentBuilder WithSignature(string name, bool? isSigned, int? page = 1)
    {
        AddField(name, FieldKind.Signature, isSigned: isSigned, page: page);
        return this;
    }

    public NormalizedDocument Build() => _document;

    public static ValidationContext ContextFor(params NormalizedDocument[] documents) =>
        new(Guid.NewGuid(), WorkflowType.FloodplainDevelopmentPermit, documents);

    private void AddField(string name, FieldKind kind, string? value = null, bool? isChecked = null, bool? isSigned = null, int? page = 1)
    {
        _document.Fields.Add(new DocumentField
        {
            Id = Guid.NewGuid(),
            Name = name,
            Value = value,
            Kind = kind,
            IsChecked = isChecked,
            IsSigned = isSigned,
            Confidence = 0.95,
            PageNumber = page,
        });
    }
}
