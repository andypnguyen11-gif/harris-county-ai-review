# Initial Workflow: Harris County Floodplain Development Permit

This document defines the first review workflow the validation engine supports and maps each real
county requirement to a deterministic rule, a deferred semantic check, or an out-of-scope item.
It is derived from the public Harris County Engineering Department / Office of the County Engineer
(OCE) documents in the reference corpus.

## Selected workflow

**Floodplain development permit, filed via the Residential Application pathway**
(`WorkflowType.FloodplainDevelopmentPermit`, implemented by `FloodplainDevelopmentPermitWorkflow`).

There is no standalone "floodplain permit application" — the floodplain development permit **is**
the county development permit. A permit is required for all development in unincorporated Harris
County (`HC_Floodplain_Management_Regulations_current.pdf`, Sec. 4.01), and intake for residential
projects is the paper **Residential Development Permit Application** (v1.6, Jan 2016,
`HC_Residential_Development_Permit_Application.pdf`) or the equivalent ePermits online form.
Commercial projects follow the separate Commercial Site Development pathway with sealed civil plans
(`HC_Civil_Review_Checklist_2024.pdf`) and are out of scope for the MVP.

## Class I vs Class II

The regulations issue two permit classes (Regs Secs. 4.05–4.07):

| Class | Applies when | Extra document burden |
|---|---|---|
| **Class I** | Land entirely outside the mapped 1% (100-year) floodplain and above the BFE, including Shaded X where the lowest adjacent grade is above the BFE, and CLOMA/CLOMR-elevated sites (Sec. 4.06) | Application + site plan/drawings |
| **Class II** | Land in or partially in any A zone, below the 100-year flood elevation, in a floodway, or in any V zone (Sec. 4.07) | Adds sealed plan sets, topographic survey / FEMA Elevation Certificate, floodplain notes on foundation drawings, As-Built Certificate, and elevation-related certifications |

The submitted documents do not carry a machine-readable "Class I/II" flag, and determining the
class requires flood-zone knowledge (FIRM panel lookup) that the deterministic engine does not
have. The workflow therefore models the **unconditional core set** deterministically and surfaces
the class-dependent elevation certificate as `NeedsHumanReview` when absent, instead of guessing.

## Deterministic rules in `FloodplainDevelopmentPermitWorkflow`

### Required documents (Regs Sec. 4.04)

| Requirement | Rule | Behavior when absent | Source |
|---|---|---|---|
| Development permit application | `RequiredDocumentRule(PermitApplication)` | `Missing` | Regs 4.04 chapeau; the application form itself |
| Site plan (two copies, to scale or fully dimensioned, including the drawing of the shape/size of the development) | `RequiredDocumentRule(SitePlan)` | `Missing` | Regs 4.04(a)–(b) |
| FEMA Elevation Certificate (FF-206-FY-22-152) | `RequiredDocumentRule(ElevationCertificate)` | `NeedsHumanReview` — required for Class II (100-yr floodplain/floodway, A/V zones) and Shaded X submissions; class is not deterministically knowable | `HC_Residential_Floodplain_Notes_2017.pdf` Special Requirements (a); `HC_Permits_FAQ.html`; Regs 4.05(a)(2) |

### Required fields on the Residential Application

All field rules are scoped to the `PermitApplication` document and configured with OCR name
variants (matching is case-, whitespace-, and punctuation-insensitive).

| Requirement | Rule | Field + variants |
|---|---|---|
| Property address | `RequiredFieldRule` | `ADDRESS`, `Property Address`, `Site Address`, `Project Address` |
| HCAD account number | `RequiredFieldRule` | `HCAD Account Number`, `HCAD #`, `HCAD Acct No`, `HCAD Account #` |
| Owner name | `RequiredFieldRule` | `OWNER NAME`, `Owner`, `Name of Owner`, `Property Owner` |
| Applicant printed name | `RequiredFieldRule` | `Print Name`, `Print Name (Applicant)`, `Printed Name`, `Applicant Name` |
| Fill acknowledgement initials (Texas Water Code §11.086 acknowledgement) | `RequiredFieldRule` | `Initials`, `Fill Acknowledgement Initials`, `Acknowledgement Initials`, `Applicant Initials` |

### Signature and date

| Requirement | Rule | Configuration |
|---|---|---|
| Applicant signature | `SignatureRule` | `Signature`, `Applicant Signature`, `Signature (Applicant)`, `Signature of Applicant`; signed → `Complete`, unsigned or absent → `Missing` |
| Application date | `DateRule` | `Date`, `Application Date`, `Date Signed`, `Signature Date`; must parse as a date and must not be in the future |

### Checkbox sections ("Check all applicable boxes")

Each rule requires **at least one** box in its section to be checked (application form, p. 1).

| Requirement | Rule | Checkboxes |
|---|---|---|
| Construction type selection | `CheckboxRule` (group) | Single Family Dwelling (includes garage), Manufactured Home/Mobile Home, Fill, Repair/Remodel of Existing Bldg., Expansion of Exist. Bldg., Swimming Pool, Accessory Building, Town Home (1–3 Units), Other |
| Driveway status selection | `CheckboxRule` (group) | Existing Driveway (no new construction or expansion), New Driveway, Addition, Paving over existing culvert |
| Sewer and water system selection | `CheckboxRule` (group) | Public Water & Sewer System, Private Water & Sewer System, Public Utilities District, Water Well, Proposed Septic, Existing Septic |
| Building code selection | `CheckboxRule` (group) | 2006 IRC, City of Houston IRC |

## Conditional requirements noted but not yet enforced

These are real requirements whose trigger condition is knowable from extracted data; they are
candidates for follow-up deterministic rules (the `CheckboxRule`/rule framework already supports
applicability conditions):

- Construction-type box checked ⇒ its companion Sq. Ft. / Est. Cost / Qty. Cu. Yds. / # of Units
  fields present (application form).
- Fill box checked ⇒ quantity in cubic yards present (application form).
- Existing Septic checked ⇒ year installed and license number present (application form).
- New driveway / addition ⇒ width at property line, number of approaches, culvert length, and
  cross-street fields present; street maintenance (county vs private) and material
  (concrete/asphalt/gravel) selections (application form).
- Class II packages ⇒ three sealed plan sets, topographic survey, floodplain notes and designer
  certification on foundation drawings, As-Built Certificate before occupancy
  (Regs 4.04(e); `HC_Residential_Floodplain_Notes_2017.pdf`; `HC_AsBuilt_Certificate_2025.pdf`).
- Elevation arithmetic (FFE vs BFE/500-yr level/crown of street) once elevations are extracted
  (Regs 4.07(b)(1); `HC_Foundation_Certificate_2025.pdf`).
- Cross-document consistency: address/permit number identical across application, elevation
  certificate, and certificates; FIRM panel and datum consistency
  (`HC_Civil_Review_Checklist_2024.pdf`).

## Deferred to semantic (LLM) validation

Judgment calls that deterministic rules must not attempt (see the core engineering principle in
`CLAUDE.md`):

- Adequacy of the "Describe use of Accessory Building or Other" free-text description.
- Site-plan sufficiency — "sufficient description to locate the property", to-scale or
  "sufficient dimensioning" (Regs 4.04(a)) is a qualitative standard.
- Benchmark description completeness on drawings (`HC_Residential_Floodplain_Notes_2017.pdf`).
- Designer certification wording vs the required statement (Floodplain Notes NOTE block).
- Substantial Improvement / Substantial Damage reasoning (Regs 4.04(e)(3), 5.01–5.03).
- Consistency of narrative descriptions vs checked boxes (e.g. fill described but Fill unchecked).
- HCFCD/PCPM compliance evidence and insignificant-development waiver eligibility (Regs 4.04(e),
  4.05(b)(1)).

## Out of scope for the MVP workflow

- Commercial Site Development pathway (`HC_Commercial_Site_Development_page.html`,
  `HC_Civil_Review_Checklist_2024.pdf`).
- Post-permit lifecycle documents: staged elevation certificates 2 and 3, As-Built Certificate,
  Foundation Certificate, Floodproofing Certificate (they arrive after permit issuance).
- Fee computation and payment verification (`HC_Fire_Code_and_Permits_Fee_Schedule_2022.pdf`).
- Class determination from FIRM/flood-zone data, and prohibited-condition flags such as fill used
  to elevate a structure in the 100-year floodplain (Regs 4.07(b)(9)) or floodway encroachment
  (Regs 4.07(a)).

## Source documents

- `HC_Floodplain_Management_Regulations_current.pdf` — Secs. 4.01–4.07, 9.04
- `HC_Residential_Development_Permit_Application.pdf` — v1.6, Jan 2016, field set
- `HC_Residential_Floodplain_Notes_2017.pdf` — special requirements, foundation drawing notes
- `HC_Permits_FAQ.html` — 100-yr and 500-yr floodplain requirements
- `HC_Floodplain_Management_page.html` — OCE floodplain management overview
- `FEMA_Elevation_Certificate_ff-206-fy22-152.pdf` — elevation certificate sections
- `HC_AsBuilt_Certificate_2025.pdf`, `HC_Foundation_Certificate_2025.pdf` — post-permit certificates
- `HC_Civil_Review_Checklist_2024.pdf` — commercial pathway (out of scope)
- `HC_Fire_Code_and_Permits_Fee_Schedule_2022.pdf` — fees (out of scope)
