using HarrisCountyAI.Application.Search.Retrieval;
using HarrisCountyAI.Application.Validation.Semantic;
using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarrisCountyAI.Application.Validation.Comparison;

/// <summary>
/// Compares a case's submitted documents against a workflow's requirements.
///
/// The ordering is the point of this service. For every requirement the
/// deterministic stage runs first and, whenever it can reach a conclusion,
/// that conclusion is the answer — a missing document, a missing field, a
/// blank signature are all facts, and facts are settled in code. The language
/// model is reached for exactly one situation: a requirement whose mechanical
/// checks have all passed and which carries a
/// <see cref="Requirement.SemanticCriterion"/>, meaning the remaining question
/// is genuinely "does what was submitted actually satisfy this?". A
/// requirement the deterministic stage has already failed never reaches a
/// model — there is nothing for judgment to add, and asking would invite a
/// model to talk its way past a hard fact.
/// </summary>
public sealed class RequirementComparisonService : IRequirementComparisonService
{
    /// <summary>Cap on the corpus excerpt attached to each requirement.</summary>
    private const int MaxExcerptLength = 1000;

    /// <summary>Cap on the submitted text handed to a semantic evaluation.</summary>
    private const int MaxSemanticContentLength = 8000;

    private readonly IReadOnlyList<IRequirementCatalog> _catalogs;
    private readonly IRetrievalService _retrievalService;
    private readonly ISemanticValidationService _semanticValidation;
    private readonly ILogger<RequirementComparisonService> _logger;

    public RequirementComparisonService(
        IEnumerable<IRequirementCatalog> catalogs,
        IRetrievalService retrievalService,
        ISemanticValidationService semanticValidation,
        ILogger<RequirementComparisonService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(retrievalService);
        ArgumentNullException.ThrowIfNull(semanticValidation);

        _catalogs = [.. catalogs];
        _retrievalService = retrievalService;
        _semanticValidation = semanticValidation;
        _logger = logger ?? NullLogger<RequirementComparisonService>.Instance;
    }

    public async Task<IReadOnlyList<RequirementComparisonResult>> CompareAsync(
        RequirementComparisonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Documents);

        var catalog = _catalogs.FirstOrDefault(entry => entry.WorkflowType == request.WorkflowType)
            ?? throw new InvalidOperationException(
                $"No requirement catalog is registered for workflow {request.WorkflowType}.");

        var context = new ValidationContext(request.CaseId, request.WorkflowType, request.Documents);
        var results = new List<RequirementComparisonResult>();

        foreach (var requirement in catalog.GetRequirements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await CompareRequirementAsync(requirement, context, request, cancellationToken));
        }

        _logger.LogInformation(
            "Compared {RequirementCount} requirements for case {CaseId}; {SemanticCount} needed semantic evaluation.",
            results.Count,
            request.CaseId,
            results.Count(result => result.EvaluatedBy == ValidationType.Semantic));

        return results;
    }

    private async Task<RequirementComparisonResult> CompareRequirementAsync(
        Requirement requirement,
        ValidationContext context,
        RequirementComparisonRequest request,
        CancellationToken cancellationToken)
    {
        // Step 1 — deterministic. Always runs, always first.
        var deterministic = EvaluateDeterministically(requirement, context);

        // Requirement evidence is for the reviewer's benefit; it never feeds a
        // verdict, so a retrieval failure must not sink the comparison.
        var requirementEvidence = request.IncludeRequirementEvidence
            ? await RetrieveRequirementEvidenceAsync(requirement, request, cancellationToken)
            : [];

        // Step 2 — semantic, and only for what step 1 deliberately left open.
        if (!deterministic.NeedsSemanticJudgment)
        {
            return Build(requirement, deterministic, deterministic.Status, requirementEvidence);
        }

        if (!request.AllowSemanticEvaluation)
        {
            return Build(
                requirement,
                deterministic,
                ValidationStatus.NeedsHumanReview,
                requirementEvidence,
                message: $"'{requirement.Label}' is present, but judging whether it satisfies the "
                    + "requirement needs review; semantic evaluation is disabled for this comparison.");
        }

        var evaluation = await _semanticValidation.EvaluateAsync(
            new SemanticValidationRequest
            {
                Requirement = requirement.Label,
                RequirementDescription = requirement.SemanticCriterion!,
                DocumentText = Cap(deterministic.SemanticContent!, MaxSemanticContentLength),
            },
            cancellationToken);

        return Build(
            requirement,
            deterministic,
            evaluation.Verdict switch
            {
                SemanticVerdict.Pass => ValidationStatus.Complete,
                SemanticVerdict.Fail => ValidationStatus.Invalid,
                SemanticVerdict.NeedsHumanReview => ValidationStatus.NeedsHumanReview,
                _ => ValidationStatus.UnableToDetermine,
            },
            requirementEvidence,
            message: evaluation.Reasoning,
            evaluatedBy: ValidationType.Semantic,
            promptVersion: evaluation.PromptVersion,
            modelDeployment: evaluation.ModelDeployment);
    }

    /// <summary>
    /// Settles everything about a requirement that is a matter of fact:
    /// is the required document there, are the required fields present and
    /// non-blank, is the signature signed. Returns the verdict plus, when the
    /// facts all check out and a judgment criterion remains, the submitted text
    /// that judgment should be made on.
    /// </summary>
    private static DeterministicOutcome EvaluateDeterministically(
        Requirement requirement,
        ValidationContext context)
    {
        if (requirement.RequiredDocumentType is { } documentType)
        {
            var documents = context.GetDocuments(documentType);
            if (documents.Count == 0)
            {
                return requirement.IsConditional
                    ? DeterministicOutcome.Concluded(
                        ValidationStatus.NeedsHumanReview,
                        $"No {documentType} document was submitted. '{requirement.Label}' applies only in "
                        + "circumstances the submitted data does not establish, so a reviewer must confirm "
                        + "whether it was owed.",
                        [])
                    : DeterministicOutcome.Concluded(
                        ValidationStatus.Missing,
                        $"No {documentType} document was found in the submission package.",
                        []);
            }
        }

        var fieldMatch = requirement.RequiredFieldNames.Count == 0
            ? null
            : context.FindField(requirement.RequiredFieldNames, requirement.RequiredDocumentType);

        if (requirement.RequiredFieldNames.Count > 0)
        {
            if (fieldMatch is null)
            {
                return requirement.IsConditional
                    ? DeterministicOutcome.Concluded(
                        ValidationStatus.NeedsHumanReview,
                        $"'{requirement.Label}' was not found on the submission. It applies only in "
                        + "circumstances the submitted data does not establish, so a reviewer must confirm "
                        + "whether it was owed.",
                        [])
                    : DeterministicOutcome.Concluded(
                        ValidationStatus.Missing,
                        $"'{requirement.Label}' was not found on the submitted "
                        + $"{requirement.RequiredDocumentType?.ToString() ?? "documents"}.",
                        []);
            }

            var evidence = Evidence(fieldMatch);
            if (fieldMatch.Field.Kind == FieldKind.Signature)
            {
                if (fieldMatch.Field.IsSigned != true)
                {
                    return DeterministicOutcome.Concluded(
                        ValidationStatus.Missing,
                        $"'{requirement.Label}' is present on the form but is not signed.",
                        evidence);
                }
            }
            else if (string.IsNullOrWhiteSpace(fieldMatch.Field.Value))
            {
                return DeterministicOutcome.Concluded(
                    ValidationStatus.Missing,
                    $"'{requirement.Label}' is present on the form but was left blank.",
                    evidence);
            }
        }

        var submissionEvidence = fieldMatch is null
            ? DocumentEvidence(requirement, context)
            : Evidence(fieldMatch);

        if (requirement.SemanticCriterion is null)
        {
            return DeterministicOutcome.Concluded(
                ValidationStatus.Complete,
                $"'{requirement.Label}' is satisfied by the submitted documents.",
                submissionEvidence);
        }

        // Everything mechanical checks out and a judgment remains. Hand over
        // only the submitted content the judgment is about.
        var content = fieldMatch?.Field.Value
            ?? string.Join(
                "\n\n",
                (requirement.RequiredDocumentType is { } scoped
                    ? context.GetDocuments(scoped)
                    : context.Documents)
                .Select(document => document.RawText)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        return string.IsNullOrWhiteSpace(content)
            ? DeterministicOutcome.Concluded(
                ValidationStatus.UnableToDetermine,
                $"No submitted content is available to judge '{requirement.Label}'.",
                submissionEvidence)
            : DeterministicOutcome.NeedsJudgment(submissionEvidence, content);
    }

    private async Task<IReadOnlyList<RequirementEvidence>> RetrieveRequirementEvidenceAsync(
        Requirement requirement,
        RequirementComparisonRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // County scope, no case id: a requirement is established by the
            // county corpus and never by an applicant's own document.
            var chunks = await _retrievalService.RetrieveAsync(
                new RetrievalRequest
                {
                    Query = requirement.Description,
                    Scope = SourceType.County,
                    CaseId = null,
                    TopK = request.EvidencePerRequirement,
                },
                cancellationToken);

            return
            [
                .. chunks.Select(chunk => new RequirementEvidence
                {
                    ChunkId = chunk.ChunkId,
                    DocumentId = chunk.DocumentId,
                    Title = chunk.Title,
                    Section = chunk.Section,
                    Page = chunk.Page,
                    SourceUrl = chunk.SourceUrl,
                    Excerpt = Cap(chunk.Text, MaxExcerptLength),
                }),
            ];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not retrieve county evidence for requirement '{Requirement}'; "
                + "reporting the comparison without it.",
                requirement.Id);
            return [];
        }
    }

    private static IReadOnlyList<SubmissionEvidence> Evidence(FieldMatch match) =>
    [
        new SubmissionEvidence
        {
            DocumentId = match.Document.Id,
            DocumentType = match.Document.DocumentType,
            Page = match.Field.PageNumber,
            FieldName = match.Field.Name,
            ExtractedValue = match.Field.Value,
        },
    ];

    private static IReadOnlyList<SubmissionEvidence> DocumentEvidence(
        Requirement requirement,
        ValidationContext context) =>
        requirement.RequiredDocumentType is { } documentType
            ?
            [
                .. context.GetDocuments(documentType).Select(document => new SubmissionEvidence
                {
                    DocumentId = document.Id,
                    DocumentType = document.DocumentType,
                }),
            ]
            : [];

    private static RequirementComparisonResult Build(
        Requirement requirement,
        DeterministicOutcome deterministic,
        ValidationStatus status,
        IReadOnlyList<RequirementEvidence> requirementEvidence,
        string? message = null,
        ValidationType evaluatedBy = ValidationType.Deterministic,
        string? promptVersion = null,
        string? modelDeployment = null) => new()
        {
            Requirement = requirement,
            Status = status,
            Message = message ?? deterministic.Message,
            EvaluatedBy = evaluatedBy,
            DeterministicStatus = deterministic.Status,
            RequirementEvidence = requirementEvidence,
            SubmissionEvidence = deterministic.SubmissionEvidence,
            PromptVersion = promptVersion,
            ModelDeployment = modelDeployment,
        };

    private static string Cap(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    /// <summary>
    /// What the deterministic stage concluded. Either it decided
    /// (<see cref="NeedsSemanticJudgment"/> false, and <see cref="Status"/>
    /// is the answer), or it verified every fact and deliberately left the
    /// judgment open, in which case <see cref="SemanticContent"/> carries the
    /// submitted text the judgment is about.
    /// </summary>
    private sealed record DeterministicOutcome(
        ValidationStatus Status,
        string Message,
        IReadOnlyList<SubmissionEvidence> SubmissionEvidence,
        bool NeedsSemanticJudgment,
        string? SemanticContent)
    {
        public static DeterministicOutcome Concluded(
            ValidationStatus status,
            string message,
            IReadOnlyList<SubmissionEvidence> evidence) =>
            new(status, message, evidence, NeedsSemanticJudgment: false, SemanticContent: null);

        public static DeterministicOutcome NeedsJudgment(
            IReadOnlyList<SubmissionEvidence> evidence,
            string content) =>
            new(
                // Everything code can check has passed; only judgment remains.
                ValidationStatus.Complete,
                "Deterministic checks passed; the remaining judgment needs semantic evaluation.",
                evidence,
                NeedsSemanticJudgment: true,
                SemanticContent: content);
    }
}
