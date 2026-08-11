using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Domain.Entities;

/// <summary>A document review case tracking an applicant submission through a workflow.</summary>
public class Case
{
    private readonly List<Document> _documents = [];

    private Case(Guid id, string caseNumber, string name, WorkflowType workflowType, CaseStatus status, DateTime createdAt, DateTime updatedAt)
    {
        Id = id;
        CaseNumber = caseNumber;
        Name = name;
        WorkflowType = workflowType;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Human-readable case identifier, e.g. "HC-2026-0001".</summary>
    public string CaseNumber { get; private set; }

    public string Name { get; private set; }

    public WorkflowType WorkflowType { get; private set; }

    public CaseStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyCollection<Document> Documents => _documents.AsReadOnly();

    public static Case Create(string caseNumber, string name, WorkflowType workflowType)
    {
        if (string.IsNullOrWhiteSpace(caseNumber))
        {
            throw new ArgumentException("Case number is required.", nameof(caseNumber));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Case name is required.", nameof(name));
        }

        var now = DateTime.UtcNow;
        return new Case(Guid.NewGuid(), caseNumber.Trim(), name.Trim(), workflowType, CaseStatus.New, now, now);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Case name is required.", nameof(name));
        }

        Name = name.Trim();
        Touch();
    }

    public void SetStatus(CaseStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown case status.");
        }

        if (Status == status)
        {
            return;
        }

        Status = status;
        Touch();
    }

    public Document AddDocument(string fileName, string blobPath, DocumentType documentType)
    {
        var document = Document.Create(Id, fileName, blobPath, documentType);
        _documents.Add(document);
        Touch();
        return document;
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
