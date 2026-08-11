using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.Application.Validation.Comparison;

/// <summary>
/// The requirements for the Harris County development (floodplain) permit,
/// transcribed from the Floodplain Management Regulations and the Residential
/// Development Permit Application form (v1.6, Jan 2016). Same sources as
/// <see cref="Workflows.FloodplainDevelopmentPermitWorkflow"/>, stated at the
/// granularity a reviewer thinks in — "is there a site plan?" — rather than at
/// the granularity of individual rules.
/// </summary>
/// <remarks>
/// Only two requirements carry a semantic criterion, and both are cases where
/// presence genuinely does not settle the question: whether a narrative
/// description matches the work actually checked off, and whether a free-text
/// use description says anything a reviewer can act on. Everything else is
/// decided in code.
/// </remarks>
public sealed class FloodplainDevelopmentPermitRequirements : IRequirementCatalog
{
    public WorkflowType WorkflowType => WorkflowType.FloodplainDevelopmentPermit;

    public IReadOnlyList<Requirement> GetRequirements() =>
    [
        new Requirement
        {
            Id = "permit-application",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "Development permit application",
            Description = "Every development permit submission must include a completed "
                + "Harris County development permit application.",
            SourceReference = "Floodplain Management Regulations Sec. 4.04(a)",
            RequiredDocumentType = DocumentType.PermitApplication,
        },
        new Requirement
        {
            Id = "site-plan",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "Site plan",
            Description = "Every development permit submission must include a site plan showing "
                + "the proposed development on the property.",
            SourceReference = "Floodplain Management Regulations Sec. 4.04(b)",
            RequiredDocumentType = DocumentType.SitePlan,
        },
        new Requirement
        {
            Id = "elevation-certificate",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "FEMA Elevation Certificate",
            Description = "A FEMA Elevation Certificate is required for Class II submissions "
                + "(100-year floodplain, floodway, A/V zones) and for the Shaded X (500-year) "
                + "floodplain.",
            SourceReference = "Floodplain Management Regulations Secs. 4.05-4.07",
            RequiredDocumentType = DocumentType.ElevationCertificate,
            // The permit class cannot be derived from extracted data, so its
            // absence is a question for a reviewer, not a finding of omission.
            IsConditional = true,
        },
        new Requirement
        {
            Id = "property-address",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "Property address",
            Description = "The application must state the address of the property the work is proposed on.",
            SourceReference = "Residential Development Permit Application",
            RequiredDocumentType = DocumentType.PermitApplication,
            RequiredFieldNames = ["ADDRESS", "Property Address", "Site Address", "Project Address"],
        },
        new Requirement
        {
            Id = "hcad-account-number",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "HCAD account number",
            Description = "The application must state the HCAD account number of the property.",
            SourceReference = "Residential Development Permit Application",
            RequiredDocumentType = DocumentType.PermitApplication,
            RequiredFieldNames = ["HCAD Account Number", "HCAD #", "HCAD Acct No", "HCAD Account #"],
        },
        new Requirement
        {
            Id = "owner-name",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "Owner name",
            Description = "The application must name the owner of the property.",
            SourceReference = "Residential Development Permit Application",
            RequiredDocumentType = DocumentType.PermitApplication,
            RequiredFieldNames = ["OWNER NAME", "Owner", "Name of Owner", "Property Owner"],
        },
        new Requirement
        {
            Id = "applicant-signature",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "Applicant signature",
            Description = "The application must be signed by the applicant.",
            SourceReference = "Residential Development Permit Application",
            RequiredDocumentType = DocumentType.PermitApplication,
            RequiredFieldNames = ["Signature", "Applicant Signature", "Signature (Applicant)", "Signature of Applicant"],
        },
        new Requirement
        {
            Id = "application-date",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "Application date",
            Description = "The application must be dated by the applicant.",
            SourceReference = "Residential Development Permit Application",
            RequiredDocumentType = DocumentType.PermitApplication,
            RequiredFieldNames = ["Date", "Application Date", "Date Signed", "Signature Date"],
        },
        new Requirement
        {
            Id = "project-description-consistency",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "Project description consistency with construction type",
            Description = "The narrative description of the proposed work must be consistent with the "
                + "construction type boxes checked on the application.",
            SourceReference = "Residential Development Permit Application",
            RequiredDocumentType = DocumentType.PermitApplication,
            RequiredFieldNames =
            [
                "Project Description",
                "Description of Work",
                "Describe the Work",
                "Scope of Work",
                "Description",
            ],
            SemanticCriterion = "The narrative description of the proposed work must be consistent with the "
                + "construction type boxes checked on the Residential Development Permit Application. Work "
                + "that the description mentions (for example fill placement, a swimming pool, or an "
                + "accessory building) should have its corresponding box checked, and every checked box "
                + "should plausibly correspond to work in the description. Evaluate only the consistency "
                + "between the description and the checked boxes.",
        },
        new Requirement
        {
            Id = "accessory-use-description",
            WorkflowType = WorkflowType.FloodplainDevelopmentPermit,
            Label = "Accessory building or other use description",
            Description = "When the Accessory Building or Other construction type is checked, the "
                + "application must describe the intended use.",
            SourceReference = "Residential Development Permit Application",
            RequiredDocumentType = DocumentType.PermitApplication,
            RequiredFieldNames =
            [
                "Describe use of Accessory Building or Other",
                "Describe Use",
                "Description of Use",
                "Use of Accessory Building or Other",
            ],
            SemanticCriterion = "The description must be specific enough for a reviewer to understand what "
                + "the accessory building or other work will be used for (for example 'detached workshop "
                + "for personal woodworking'); a placeholder or meaningless entry (such as 'N/A', 'stuff', "
                + "or 'building') does not satisfy the requirement.",
            // Only owed when the applicant checked Accessory Building or Other,
            // which the deterministic stage cannot infer from the field alone.
            IsConditional = true,
        },
    ];
}
