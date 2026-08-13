using System.Globalization;
using HarrisCountyAI.Application.Documents.Extraction;
using HarrisCountyAI.Domain.ValueObjects;

namespace HarrisCountyAI.IntegrationTests.EndToEnd;

/// <summary>
/// Extraction results shaped like real submissions to the Harris County
/// Residential Development Permit Application (v1.6, Jan 2016): the printed
/// field labels as OCR renders them, checkboxes as selection marks, and page
/// text a reviewer would recognize.
/// </summary>
/// <remarks>
/// These enter the system as <see cref="ExtractedDocument"/>s — the boundary
/// the real Document Intelligence client produces — so everything downstream
/// (normalization, field classification, validation, chunking, indexing) runs
/// exactly as it does in production.
/// </remarks>
internal static class FloodplainSubmission
{
    /// <summary>A date safely in the past, so the application-date rule's future check is not what a test is measuring.</summary>
    public static string RecentDate =>
        DateTime.UtcNow.AddDays(-14).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

    /// <summary>A date the OCR could not resolve into a real calendar date.</summary>
    public const string UnreadableDate = "13/45/2026";

    /// <summary>
    /// The permit application itself. Defaults produce a submission that
    /// satisfies every deterministic application rule; each parameter takes one
    /// requirement away or adds content a semantic rule reacts to.
    /// </summary>
    public static ExtractedDocument PermitApplication(
        Guid documentId,
        bool signed = true,
        string? applicationDate = null,
        bool constructionTypeChecked = true,
        bool buildingCodeChecked = true,
        string? projectDescription = null,
        string? accessoryUseDescription = null,
        bool accessoryBuildingChecked = false,
        string? appendedPageText = null)
    {
        var date = applicationDate ?? RecentDate;

        List<ExtractedField> fields =
        [
            Field("ADDRESS", "4732 Cypresswood Dr, Spring, TX 77379"),
            Field("HCAD ACCOUNT NUMBER", "1234567890123"),
            Field(
                "OWNER NAME",
                "Jane P. Smith",
                valueBox: new BoundingBox { PageNumber = 1, X = 0.12, Y = 0.34, Width = 0.56, Height = 0.07 }),
            Field("PRINT NAME", "Robert Chen"),
            Field("Initials", "RC"),
            // A signature field with no captured value reads as unsigned.
            Field("SIGNATURE (APPLICANT)", signed ? "Robert Chen" : null),
            Field("DATE", date),
        ];

        if (projectDescription is not null)
        {
            fields.Add(Field("Project Description", projectDescription));
        }

        if (accessoryUseDescription is not null)
        {
            fields.Add(Field("Describe use of Accessory Building or Other", accessoryUseDescription));
        }

        List<ExtractedSelectionMark> marks =
        [
            Mark("Single Family Dwelling (includes garage)", constructionTypeChecked),
            Mark("Fill", false),
            Mark("Accessory Building", accessoryBuildingChecked),
            Mark("Existing Driveway (no new construction or expansion)", true),
            Mark("Public Water & Sewer System", true),
            Mark("2006 IRC", buildingCodeChecked),
            Mark("City of Houston IRC", false),
        ];

        var pageOne =
            "HARRIS COUNTY RESIDENTIAL DEVELOPMENT PERMIT APPLICATION\n"
            + "Property address: 4732 Cypresswood Dr, Spring, TX 77379\n"
            + "HCAD account number: 1234567890123\n"
            + "Owner name: Jane P. Smith\n"
            + $"Applicant: Robert Chen. Application date: {date}.\n"
            + "Construction type: single family dwelling including attached garage.";

        var pageTwo =
            "APPLICANT CERTIFICATION\n"
            + (signed
                ? "Signed by Robert Chen, applicant of record."
                : "The applicant certification block was left blank.")
            + "\nDriveway: existing, no new construction or expansion. "
            + "Water and sewer: public system. Building code: 2006 IRC.\n"
            + (projectDescription is null ? string.Empty : $"Description of work: {projectDescription}\n")
            + (appendedPageText ?? string.Empty);

        return Document(documentId, pageOne, pageTwo, fields, marks);
    }

    /// <summary>A site plan sheet — no form fields, just drawing notes.</summary>
    public static ExtractedDocument SitePlan(Guid documentId, string? appendedPageText = null) =>
        Document(
            documentId,
            "SITE PLAN - SHEET C-1\n"
            + "Lot 14, Block 3, Cypresswood Estates. Scale 1 inch = 20 feet.\n"
            + "Proposed residence footprint 2,400 square feet, setback 25 feet from the front property line.",
            "SITE PLAN - SHEET C-2\n"
            + "Existing drainage flows to the roadside ditch along Cypresswood Dr.\n"
            + "Benchmark: Harris County monument HC-4471, elevation 81.22 ft NAVD88.\n"
            + (appendedPageText ?? string.Empty),
            fields: [],
            marks: []);

    /// <summary>A FEMA elevation certificate.</summary>
    public static ExtractedDocument ElevationCertificate(Guid documentId) =>
        Document(
            documentId,
            "FEMA ELEVATION CERTIFICATE - SECTION A\n"
            + "Building owner: Jane P. Smith. Building use: residential.\n"
            + "Flood zone: AE. Base flood elevation: 77.4 ft NAVD88.",
            "FEMA ELEVATION CERTIFICATE - SECTION C\n"
            + "Lowest floor elevation: 78.4 ft NAVD88, one foot above the base flood elevation.\n"
            + "Certified by a licensed professional engineer.",
            fields: [],
            marks: []);

    /// <summary>The text of the county reference passage the corpus tests ingest.</summary>
    public const string CountyRegulationText =
        "SECTION 4.04 - APPLICATION REQUIREMENTS\n"
        + "Every application for a development permit shall be accompanied by a completed "
        + "development permit application form and a site plan drawn to scale showing the "
        + "location and dimensions of the proposed development.\n"
        + "SECTION 4.06 - ELEVATION CERTIFICATE\n"
        + "A FEMA Elevation Certificate is required for Class II development within the "
        + "100-year floodplain, a floodway, or an A or V zone.";

    private static ExtractedDocument Document(
        Guid documentId,
        string pageOne,
        string pageTwo,
        IReadOnlyList<ExtractedField> fields,
        IReadOnlyList<ExtractedSelectionMark> marks) => new()
        {
            DocumentId = documentId,
            Pages =
            [
                new ExtractedPage { PageNumber = 1, Text = pageOne },
                new ExtractedPage { PageNumber = 2, Text = pageTwo },
            ],
            KeyValuePairs = fields,
            SelectionMarks = marks,
            RawText = $"{pageOne}\n{pageTwo}",
            ModelId = "stub-extraction-model",
            ExtractedAt = DateTime.UtcNow,
        };

    private static ExtractedField Field(string key, string? value, BoundingBox? valueBox = null) => new()
    {
        Key = key,
        Value = value,
        Confidence = 0.95,
        PageNumber = 1,
        ValueBoundingBox = valueBox,
    };

    private static ExtractedSelectionMark Mark(string name, bool selected) => new()
    {
        Name = name,
        IsSelected = selected,
        Confidence = 0.98,
        PageNumber = 1,
    };
}
