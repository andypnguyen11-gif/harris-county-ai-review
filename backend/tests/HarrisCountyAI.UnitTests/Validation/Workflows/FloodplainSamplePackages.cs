using HarrisCountyAI.Domain.Entities;
using HarrisCountyAI.Domain.Enums;

namespace HarrisCountyAI.UnitTests.Validation.Workflows;

/// <summary>
/// In-memory sample submission packages for the floodplain development permit workflow, with field
/// names rendered the way OCR reads the Residential Development Permit Application (v1.6, Jan 2016).
/// </summary>
public static class FloodplainSamplePackages
{
    /// <summary>A fully valid Class II-style package: application, site plan, and elevation certificate, every requirement satisfied.</summary>
    public static NormalizedDocument[] CompletePackage(Guid? caseId = null)
    {
        var id = caseId ?? Guid.NewGuid();
        return
        [
            Application(id, signature: true, date: "06/01/2026", constructionTypeChecked: true, buildingCodeChecked: true),
            new NormalizedDocumentBuilder(DocumentType.SitePlan, id).Build(),
            new NormalizedDocumentBuilder(DocumentType.ElevationCertificate, id).Build(),
        ];
    }

    /// <summary>An incomplete package: the applicant signature is unsigned and the elevation certificate is missing entirely.</summary>
    public static NormalizedDocument[] IncompletePackage(Guid? caseId = null)
    {
        var id = caseId ?? Guid.NewGuid();
        return
        [
            Application(id, signature: false, date: "06/01/2026", constructionTypeChecked: true, buildingCodeChecked: true),
            new NormalizedDocumentBuilder(DocumentType.SitePlan, id).Build(),
        ];
    }

    /// <summary>An invalid package: all documents present, but the application date is unreadable and required checkbox sections are unchecked.</summary>
    public static NormalizedDocument[] InvalidPackage(Guid? caseId = null)
    {
        var id = caseId ?? Guid.NewGuid();
        return
        [
            Application(id, signature: true, date: "13/45/2026", constructionTypeChecked: false, buildingCodeChecked: false),
            new NormalizedDocumentBuilder(DocumentType.SitePlan, id).Build(),
            new NormalizedDocumentBuilder(DocumentType.ElevationCertificate, id).Build(),
        ];
    }

    private static NormalizedDocument Application(
        Guid caseId,
        bool signature,
        string date,
        bool constructionTypeChecked,
        bool buildingCodeChecked) =>
        new NormalizedDocumentBuilder(DocumentType.PermitApplication, caseId)
            .WithTextField("ADDRESS", "4732 Cypresswood Dr, Spring, TX 77379")
            .WithTextField("HCAD ACCOUNT NUMBER", "1234567890123")
            .WithTextField("OWNER NAME", "Jane P. Smith")
            .WithTextField("PRINT NAME", "Robert Chen")
            .WithTextField("Initials", "RC")
            .WithSignature("SIGNATURE (APPLICANT)", isSigned: signature)
            .WithDateField("DATE", date)
            .WithCheckbox("Single Family Dwelling (includes garage)", isChecked: constructionTypeChecked)
            .WithCheckbox("Fill", isChecked: false)
            .WithCheckbox("Existing Driveway (no new construction or expansion)", isChecked: true)
            .WithCheckbox("Public Water & Sewer System", isChecked: true)
            .WithCheckbox("2006 IRC", isChecked: buildingCodeChecked)
            .WithCheckbox("City of Houston IRC", isChecked: false)
            .Build();
}
