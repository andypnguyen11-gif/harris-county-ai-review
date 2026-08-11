using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Application.Validation.Semantic;
using HarrisCountyAI.Domain.Enums;
using HarrisCountyAI.Domain.Validation;

namespace HarrisCountyAI.Application.Validation.Workflows;

/// <summary>
/// Requirements for the Harris County development (floodplain) permit filed via the Residential
/// Application pathway. The floodplain development permit IS the county development permit:
/// Class I outside the 100-year floodplain, Class II in A/V zones, a floodway, or below the BFE
/// (Floodplain Management Regulations Secs. 4.05-4.07).
///
/// Documents: the application itself and a site plan are required for every submission
/// (Regs Sec. 4.04(a)-(b)). A FEMA Elevation Certificate is required for Class II / Shaded X
/// submissions; because the permit class cannot be derived deterministically from extracted data,
/// its absence surfaces as NeedsHumanReview rather than Missing.
///
/// Fields, signatures, dates, and checkbox sections mirror the Residential Development Permit
/// Application form (v1.6, Jan 2016). Field name variants cover how OCR typically renders the
/// printed labels. See docs/architecture/initial-workflow.md for the full mapping and sources.
///
/// The rule set has two clearly separated sections: the deterministic rules
/// (<see cref="BuildDeterministicRules"/>) that never involve a model, and the semantic rules
/// (<see cref="BuildSemanticRules"/>) for judgment calls that deterministic code must not
/// attempt. Semantic rules exist only when the workflow is constructed with an
/// <see cref="ISemanticValidationService"/>.
/// </summary>
public sealed class FloodplainDevelopmentPermitWorkflow : IWorkflowDefinition
{
    private static readonly string[] NarrativeDescriptionVariants =
    [
        "Project Description",
        "Description of Work",
        "Describe the Work",
        "Scope of Work",
        "Description",
        "Describe use of Accessory Building or Other",
    ];

    private static readonly string[] AccessoryUseDescriptionVariants =
    [
        "Describe use of Accessory Building or Other",
        "Describe Use",
        "Description of Use",
        "Use of Accessory Building or Other",
    ];

    private static readonly IReadOnlyList<IReadOnlyCollection<string>> ConstructionTypeCheckboxes =
    [
        ["Single Family Dwelling", "Single Family Dwelling (includes garage)"],
        ["Manufactured Home/Mobile Home", "Manufactured Home", "Mobile Home"],
        ["Fill"],
        ["Repair/Remodel of Existing Bldg.", "Repair/Remodel of Existing Building", "Repair/Remodel"],
        ["Expansion of Exist. Bldg.", "Expansion of Existing Building"],
        ["Swimming Pool"],
        ["Accessory Building"],
        ["Town Home (1-3 Units)", "Town Home"],
        ["Other"],
    ];

    private static readonly IReadOnlyList<IReadOnlyCollection<string>> AccessoryOrOtherCheckboxes =
    [
        ["Accessory Building"],
        ["Other"],
    ];

    private readonly ISemanticValidationService? _semanticValidation;

    /// <param name="semanticValidation">
    /// When provided, the workflow includes its semantic (LLM-assisted) rules; when null the
    /// workflow is deterministic-only.
    /// </param>
    public FloodplainDevelopmentPermitWorkflow(ISemanticValidationService? semanticValidation = null)
    {
        _semanticValidation = semanticValidation;
    }

    public WorkflowType WorkflowType => WorkflowType.FloodplainDevelopmentPermit;

    public IReadOnlyList<IValidationRule> BuildRules() =>
        [.. BuildDeterministicRules(), .. BuildSemanticRules()];

    /// <summary>The deterministic rules: documents, fields, signature, date, and checkbox sections. No model involvement, ever.</summary>
    public IReadOnlyList<IValidationRule> BuildDeterministicRules() =>
    [
        // --- Required documents (Regs Sec. 4.04) ---
        new RequiredDocumentRule(
            DocumentType.PermitApplication,
            "Development permit application"),
        new RequiredDocumentRule(
            DocumentType.SitePlan,
            "Site plan"),
        new RequiredDocumentRule(
            DocumentType.ElevationCertificate,
            "FEMA Elevation Certificate",
            missingStatus: ValidationStatus.NeedsHumanReview,
            missingMessage: "No elevation certificate was submitted. One is required for Class II submissions "
                + "(100-year floodplain, floodway, A/V zones) and for the Shaded X (500-year) floodplain; "
                + "the permit class cannot be determined deterministically, so a reviewer must confirm whether it applies."),

        // --- Required fields on the Residential Application ---
        new RequiredFieldRule(
            "Property address",
            "ADDRESS",
            ["Property Address", "Site Address", "Project Address"],
            DocumentType.PermitApplication),
        new RequiredFieldRule(
            "HCAD account number",
            "HCAD Account Number",
            ["HCAD #", "HCAD Acct No", "HCAD Account #"],
            DocumentType.PermitApplication),
        new RequiredFieldRule(
            "Owner name",
            "OWNER NAME",
            ["Owner", "Name of Owner", "Property Owner"],
            DocumentType.PermitApplication),
        new RequiredFieldRule(
            "Applicant printed name",
            "Print Name",
            ["Print Name (Applicant)", "Printed Name", "Applicant Name"],
            DocumentType.PermitApplication),
        new RequiredFieldRule(
            "Fill acknowledgement initials",
            "Initials",
            ["Fill Acknowledgement Initials", "Acknowledgement Initials", "Applicant Initials"],
            DocumentType.PermitApplication),

        // --- Applicant signature and date ---
        new SignatureRule(
            "Applicant signature",
            "Signature",
            ["Applicant Signature", "Signature (Applicant)", "Signature of Applicant"],
            DocumentType.PermitApplication),
        new DateRule(
            "Application date",
            "Date",
            ["Application Date", "Date Signed", "Signature Date"],
            DocumentType.PermitApplication,
            disallowFuture: true),

        // --- Checkbox sections ("Check all applicable boxes") ---
        new CheckboxRule(
            "Construction type selection",
            ConstructionTypeCheckboxes,
            DocumentType.PermitApplication),
        new CheckboxRule(
            "Driveway status selection",
            [
                ["Existing Driveway", "Existing Driveway (no new construction or expansion)"],
                ["New Driveway"],
                ["Addition"],
                ["Paving over existing culvert"],
            ],
            DocumentType.PermitApplication),
        new CheckboxRule(
            "Sewer and water system selection",
            [
                ["Public Water & Sewer System", "Public Water and Sewer System"],
                ["Private Water & Sewer System", "Private Water and Sewer System"],
                ["Public Utilities District"],
                ["Water Well"],
                ["Proposed Septic"],
                ["Existing Septic"],
            ],
            DocumentType.PermitApplication),
        new CheckboxRule(
            "Building code selection",
            [
                ["2006 IRC"],
                ["City of Houston IRC"],
            ],
            DocumentType.PermitApplication),
    ];

    /// <summary>
    /// The semantic (LLM-assisted) rules: judgment calls the deterministic engine must not attempt
    /// (see docs/architecture/initial-workflow.md, "Deferred to semantic (LLM) validation").
    /// Empty when the workflow was constructed without an <see cref="ISemanticValidationService"/>.
    /// </summary>
    public IReadOnlyList<IValidationRule> BuildSemanticRules() =>
        _semanticValidation is null
            ? []
            :
            [
                // Consistency of the narrative description vs the checked construction-type
                // boxes (e.g. fill described but Fill unchecked) is a judgment call: the
                // description is free text and "consistent" is not a string comparison.
                new SemanticEvaluationRule(
                    "Project description consistency with construction type",
                    "The narrative description of the proposed work must be consistent with the "
                        + "construction type boxes checked on the Residential Development Permit "
                        + "Application. Work that the description mentions (for example fill placement, "
                        + "a swimming pool, or an accessory building) should have its corresponding box "
                        + "checked, and every checked box should plausibly correspond to work in the "
                        + "description. Evaluate only the consistency between the description and the "
                        + "checked boxes.",
                    _semanticValidation,
                    DocumentType.PermitApplication,
                    contentSelector: ConstructionConsistencyContent,
                    applicableWhen: context => ConstructionConsistencyContent(context) is not null),

                // Whether the "Describe use of Accessory Building or Other" free text adequately
                // describes the intended use is a qualitative standard; whether the description is
                // present at all stays deterministic (Missing without a model call).
                new SemanticEvaluationRule(
                    "Accessory building or other use description",
                    "When the applicant checks the 'Accessory Building' or 'Other' construction type "
                        + "on the Residential Development Permit Application, the form requires a "
                        + "description of the intended use. The description must be specific enough for "
                        + "a reviewer to understand what the accessory building or other work will be "
                        + "used for (for example 'detached workshop for personal woodworking'); a "
                        + "placeholder or meaningless entry (such as 'N/A', 'stuff', or 'building') "
                        + "does not satisfy the requirement.",
                    _semanticValidation,
                    DocumentType.PermitApplication,
                    contentSelector: AccessoryUseDescriptionContent,
                    applicableWhen: AccessoryOrOtherChecked,
                    missingContentStatus: ValidationStatus.Missing,
                    missingContentMessage: "The Accessory Building or Other construction type is checked, "
                        + "but no use description was found on the application."),
            ];

    /// <summary>
    /// Builds the content for the description-consistency check: the checked construction-type
    /// boxes plus the narrative description. Null (rule not applicable) when the application has
    /// no non-blank narrative description or no construction-type box is checked — the
    /// deterministic rules already cover those cases.
    /// </summary>
    private static string? ConstructionConsistencyContent(ValidationContext context)
    {
        var description = FindFieldValue(context, NarrativeDescriptionVariants);
        if (description is null)
        {
            return null;
        }

        var checkedTypes = CheckedConstructionTypes(context);
        if (checkedTypes.Count == 0)
        {
            return null;
        }

        return "Construction type boxes checked on the application: "
            + string.Join("; ", checkedTypes)
            + "\n\nApplicant's description of the work:\n"
            + description;
    }

    private static string? AccessoryUseDescriptionContent(ValidationContext context) =>
        FindFieldValue(context, AccessoryUseDescriptionVariants);

    private static bool AccessoryOrOtherChecked(ValidationContext context) =>
        CheckedBoxNames(context, AccessoryOrOtherCheckboxes).Count > 0;

    private static IReadOnlyList<string> CheckedConstructionTypes(ValidationContext context) =>
        CheckedBoxNames(context, ConstructionTypeCheckboxes);

    /// <summary>Printed names of the checked application checkboxes matching any of the given boxes' name variants.</summary>
    private static IReadOnlyList<string> CheckedBoxNames(
        ValidationContext context,
        IReadOnlyList<IReadOnlyCollection<string>> checkboxes)
    {
        var normalizedVariants = checkboxes
            .SelectMany(nameVariants => nameVariants)
            .Select(ValidationContext.NormalizeFieldName)
            .ToHashSet();

        return
        [
            .. context.GetDocuments(DocumentType.PermitApplication)
                .SelectMany(document => document.Fields)
                .Where(field => field.IsChecked == true
                    && normalizedVariants.Contains(ValidationContext.NormalizeFieldName(field.Name)))
                .Select(field => field.Name),
        ];
    }

    private static string? FindFieldValue(ValidationContext context, string[] nameVariants)
    {
        var match = context.FindField(nameVariants, DocumentType.PermitApplication);
        return string.IsNullOrWhiteSpace(match?.Field.Value) ? null : match.Field.Value.Trim();
    }
}
