using HarrisCountyAI.Application.Validation.Rules;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Validation.Workflows;

/// <summary>
/// Deterministic requirements for the Harris County development (floodplain) permit filed via the
/// Residential Application pathway. The floodplain development permit IS the county development
/// permit: Class I outside the 100-year floodplain, Class II in A/V zones, a floodway, or below
/// the BFE (Floodplain Management Regulations Secs. 4.05-4.07).
///
/// Documents: the application itself and a site plan are required for every submission
/// (Regs Sec. 4.04(a)-(b)). A FEMA Elevation Certificate is required for Class II / Shaded X
/// submissions; because the permit class cannot be derived deterministically from extracted data,
/// its absence surfaces as NeedsHumanReview rather than Missing.
///
/// Fields, signatures, dates, and checkbox sections mirror the Residential Development Permit
/// Application form (v1.6, Jan 2016). Field name variants cover how OCR typically renders the
/// printed labels. See docs/architecture/initial-workflow.md for the full mapping and sources.
/// </summary>
public sealed class FloodplainDevelopmentPermitWorkflow : IWorkflowDefinition
{
    public WorkflowType WorkflowType => WorkflowType.FloodplainDevelopmentPermit;

    public IReadOnlyList<IValidationRule> BuildRules() =>
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
            ],
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
}
