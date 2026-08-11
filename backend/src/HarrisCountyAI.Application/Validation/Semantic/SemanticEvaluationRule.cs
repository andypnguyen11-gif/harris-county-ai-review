using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.Application.Validation.Semantic;

/// <summary>
/// A validation rule whose judgment requires semantic understanding: it hands the requirement
/// description and the relevant extracted document content to <see cref="ISemanticValidationService"/>
/// and maps the model's verdict onto a <see cref="ValidationResult"/> stamped
/// <see cref="ValidationType.Semantic"/> (pass → Complete, fail → Invalid,
/// needs human review → NeedsHumanReview, anything else → UnableToDetermine, with the model's
/// short reasoning as the message).
///
/// Deterministic sub-decisions stay deterministic and never reach the model: absent documents,
/// an unmet applicability condition, and absent content are all resolved in code before any
/// model call. The rule fails closed — an unexpected error surfaces as UnableToDetermine,
/// never an exception.
/// </summary>
public sealed class SemanticEvaluationRule : IValidationRule
{
    private readonly ISemanticValidationService _semanticValidation;
    private readonly string _requirementDescription;
    private readonly DocumentType? _documentType;
    private readonly Func<ValidationContext, string?> _contentSelector;
    private readonly Func<ValidationContext, bool>? _applicableWhen;
    private readonly ValidationStatus _missingContentStatus;
    private readonly string? _missingContentMessage;

    /// <param name="requirement">Human-readable label of the requirement this rule checks.</param>
    /// <param name="requirementDescription">Trusted prompt text describing the county requirement the model evaluates against.</param>
    /// <param name="semanticValidation">The semantic evaluation service.</param>
    /// <param name="documentType">Optional document type this rule is scoped to; absent documents gate the rule off deterministically.</param>
    /// <param name="contentSelector">Selects the document content to evaluate; defaults to the raw text of the scoped documents.</param>
    /// <param name="applicableWhen">Optional deterministic applicability condition; when false the rule reports Complete without a model call.</param>
    /// <param name="missingContentStatus">Status reported when the selector yields no content (default UnableToDetermine).</param>
    /// <param name="missingContentMessage">Message reported when the selector yields no content.</param>
    public SemanticEvaluationRule(
        string requirement,
        string requirementDescription,
        ISemanticValidationService semanticValidation,
        DocumentType? documentType = null,
        Func<ValidationContext, string?>? contentSelector = null,
        Func<ValidationContext, bool>? applicableWhen = null,
        ValidationStatus missingContentStatus = ValidationStatus.UnableToDetermine,
        string? missingContentMessage = null)
    {
        if (string.IsNullOrWhiteSpace(requirement))
        {
            throw new ArgumentException("Requirement label is required.", nameof(requirement));
        }

        if (string.IsNullOrWhiteSpace(requirementDescription))
        {
            throw new ArgumentException("Requirement description is required.", nameof(requirementDescription));
        }

        ArgumentNullException.ThrowIfNull(semanticValidation);

        Requirement = requirement.Trim();
        Name = $"{nameof(SemanticEvaluationRule)}({Requirement})";
        _requirementDescription = requirementDescription.Trim();
        _semanticValidation = semanticValidation;
        _documentType = documentType;
        _contentSelector = contentSelector ?? DefaultContentSelector;
        _applicableWhen = applicableWhen;
        _missingContentStatus = missingContentStatus;
        _missingContentMessage = missingContentMessage;
    }

    /// <summary>Human-readable label of the requirement this rule instance checks.</summary>
    public string Requirement { get; }

    public string Name { get; }

    public async Task<ValidationResult> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return await EvaluateAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result(
                ValidationStatus.UnableToDetermine,
                $"Semantic evaluation of '{Requirement}' failed to run: {exception.Message}");
        }
    }

    private async Task<ValidationResult> EvaluateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        if (_applicableWhen is not null && !_applicableWhen(context))
        {
            return Result(
                ValidationStatus.Complete,
                $"Not applicable: the condition for '{Requirement}' is not met by this submission.");
        }

        if (context.Documents.Count == 0)
        {
            return Result(
                ValidationStatus.UnableToDetermine,
                $"No extracted documents are available for this case, so '{Requirement}' cannot be checked.");
        }

        if (_documentType is { } documentType && !context.HasDocumentType(documentType))
        {
            return Result(
                ValidationStatus.UnableToDetermine,
                $"No {documentType} document was submitted, so '{Requirement}' cannot be checked.");
        }

        var content = _contentSelector(context);
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result(
                _missingContentStatus,
                _missingContentMessage ?? $"No document content is available to evaluate '{Requirement}'.");
        }

        var evaluation = await _semanticValidation.EvaluateAsync(
            new SemanticValidationRequest
            {
                Requirement = Requirement,
                RequirementDescription = _requirementDescription,
                DocumentText = content,
            },
            cancellationToken);

        var sourceDocument = _documentType is { } scopedType
            ? context.GetDocuments(scopedType).FirstOrDefault()
            : null;

        return Result(
            evaluation.Verdict switch
            {
                SemanticVerdict.Pass => ValidationStatus.Complete,
                SemanticVerdict.Fail => ValidationStatus.Invalid,
                SemanticVerdict.NeedsHumanReview => ValidationStatus.NeedsHumanReview,
                _ => ValidationStatus.UnableToDetermine,
            },
            evaluation.Reasoning,
            sourceDocumentId: sourceDocument?.Id);
    }

    /// <summary>Default content: the raw text of the scoped documents (all documents when unscoped).</summary>
    private string? DefaultContentSelector(ValidationContext context)
    {
        var documents = _documentType is { } documentType
            ? context.GetDocuments(documentType)
            : context.Documents;

        var texts = documents
            .Select(document => document.RawText)
            .Where(text => !string.IsNullOrWhiteSpace(text));
        var combined = string.Join("\n\n", texts);
        return combined.Length > 0 ? combined : null;
    }

    private ValidationResult Result(ValidationStatus status, string message, Guid? sourceDocumentId = null) => new()
    {
        Requirement = Requirement,
        Status = status,
        Message = message,
        SourceDocumentId = sourceDocumentId,
        ValidationType = ValidationType.Semantic,
        RuleName = Name,
    };
}
