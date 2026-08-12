# Document region highlighting

Design for showing a validation finding's source region as a box drawn over the
submitted PDF, next to the validation report.

Status: approved, not yet implemented. Split into two PRs (PR-A backend, PR-B
frontend); see [PR split](#pr-split).

## Problem

A reviewer reading the validation report sees "Field 'HCAD account number' is
present but has no value" and must then find that field by hand on a dense
two-page permit form. The report already knows the page number, but a page is
still a lot of paper to search.

Two things are missing today:

1. **Layout.** `validation-report.html` renders `<app-document-viewer>` below
   the findings list at full width, so the reviewer scrolls away from the
   finding to look at the document.
2. **Coordinates.** Nothing in the system knows *where on the page* a finding
   came from. Every layer carries `PageNumber` and stops there.

The coordinates are not expensive to obtain — we are already paying for them.
The extraction pipeline runs Azure AI Document Intelligence `prebuilt-layout`
with the `KeyValuePairs` add-on, and its `AnalyzeResult` returns a bounding
polygon for every key, value, word, line, and selection mark.
`AnalyzeResultMapper` reads those `BoundingRegions` today but keeps only
`region.PageNumber` and discards the polygon
(`MapKeyValuePairs`, `GetPageParagraphs`, `MapTables`).

## Goals

- Show the PDF beside the validation report rather than below it.
- On opening the viewer for a document and page, box the findings on that page
  that represent problems.
- Clicking **View page** on a finding makes that finding's box the active one.
- When a finding has no locatable region, say so plainly instead of rendering a
  blank page.

## Non-goals

Deliberately excluded from this pass:

- Invented boxes for checkbox or signature findings when OCR found nothing.
- Green boxes over satisfied fields (OpenEMR's model — see [Prior art](#prior-art)).
- Multi-document synchronization beyond "jump to this document and page".
- A "show all extracted fields" mode.
- A backfill job for documents extracted before this change.

## Prior art

The `OpenEMR-Copilot-Agent` project solved a closely related problem for
clinical documents, and several decisions here are lifted from it directly:

| Adopted | Rejected |
|---|---|
| Normalized 0–1 bbox contract validated in the unit square | Server-side page rasterization to JPEG (`pypdfium2`, cached, proxied) |
| `data-citation-id` click-to-highlight, second click clears | Tesseract OCR bbox-tightening pass (`ocr_bbox.py`) |
| Color by finding status | Green boxes over every satisfied field |
| Sticky right-docked panel, stacking at 1100px | Canvas redraw on every resize |

The two rejections matter and are worth stating explicitly.

**No rasterization.** OpenEMR renders each page to a JPEG server-side and
overlays boxes on an `<img>`, because PHP's Imagick in the `openemr:flex` image
lacks Ghostscript and because it ingests TIFF, DOCX, XLSX, and HL7 alongside
PDFs. Neither constraint applies here: uploads are PDF-only
(`document-upload.html` `accept="application/pdf,.pdf"`, filtered again in
`document-upload.ts`). Rendering the real PDF client-side avoids a new
endpoint, an image cache, and the storage and CPU of a 300 DPI render, and it
stays crisp at any zoom.

**No bbox tightening.** OpenEMR needs a Tesseract pass because a vision model
returns coarse rectangles that must be relocated against OCR words. Document
Intelligence's layout model already returns tight polygons per element, so the
boxes are precise as received.

## Coordinate contract

A single value object crosses every layer:

```csharp
namespace HarrisCountyAI.Domain.ValueObjects;

/// <summary>
/// An axis-aligned region of a document page, expressed as fractions of the
/// page's width and height with the origin at the top-left corner.
/// </summary>
public sealed record BoundingBox
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
}
```

Rules, fixed:

- **Normalized at the mapper, never downstream.** Values are fractions in
  `[0, 1]`, origin top-left.
- **Unit-free by construction.** Document Intelligence reports polygons in
  inches for PDFs and pixels for images, with matching per-page `Width` and
  `Height`. Dividing by the page's own dimensions cancels the unit, so
  `DocumentPageLengthUnit` never needs to be read.
- **Quadrilateral to axis-aligned rectangle.** A polygon is eight numbers —
  four points, clockwise from top-left relative to text orientation. The box is
  the min/max over those four points, which stays correct for rotated text.
- **Degenerate input yields `null`, not a zero-size box.** A polygon with fewer
  than eight values, a page with missing or non-positive `Width`/`Height`, or a
  computed width or height of zero all produce `null`. Values are clamped to
  `[0, 1]`.
- **`PageNumber` always comes from the region that produced the chosen box.**
  Today `ExtractedField.PageNumber` is taken from the *key's* region. Once a
  rule may resolve to the *value's* box instead, page and box could disagree
  when a key and its value straddle a page break; the page must follow the box.

## Draw policy

Locked. The viewer receives a set of boxes to draw and does not decide policy.

- On open for document `D` at page `N`, draw **only findings with status
  Missing, Invalid, or PotentiallyIncomplete** that belong to `D`, sit on page
  `N`, and have a region.
- The finding whose **View page** button was clicked renders **active**
  (stronger border, higher fill). Other issue boxes on the same page stay
  visible but **inactive**.
- **Complete findings are not drawn by default.** Their resolved box is still
  persisted and still sent to the client, so click-to-highlight can use it
  without another backend change.
- Following from the two rules above: clicking **View page** on a *Complete*
  finding opens the page with that page's issue boxes drawn and **nothing
  active**. The Complete finding gets no box of its own in v1.
- A second click on the active finding clears the active state; the other
  boxes remain.

Rationale: reviewers are looking for failures. Boxing every satisfied field, as
OpenEMR does, adds noise on a dense permit form, and drawing nothing by default
would drop the requirement that missing fields highlight on open.

## Viewer contract

`DocumentViewer` is shared with the question-answering citation flow, so the
highlighting must be strictly optional.

- pdf.js replaces the `<iframe>` for **all** case PDFs, both validation
  evidence and Q&A citations.
- Boxes are an **optional input**. The validation report passes the findings for
  document `D` and page `N`; the citation flow passes none.
- **Opening a page with zero boxes is a normal, tested state**, not an
  edge case.
- County reference documents keep their existing external-link behavior; they
  are not fetched or rendered here.

## PR split

Sequenced so the backend is complete and testable before any UI exists. Both
are specified here before either is coded.

### PR-A — coordinates through the backend

No UI changes. Order of work: mapper → domain → rules → DTO.

**Domain**

- Add `Domain/ValueObjects/BoundingBox.cs` as above. `Domain/ValueObjects/` is
  a new folder; Domain currently holds `Entities`, `Enums`, `Validation`, and
  `Authorization`. Placing it in Domain lets both `DocumentField` (Domain) and
  `ExtractedField` (Application) use it, since Application references Domain.

**Extraction mapping**

- `ExtractedField` gains nullable `KeyBoundingBox` and `ValueBoundingBox`.
  They stay separate rather than pre-merged because the rules need to
  distinguish them.
- `ExtractedSelectionMark` gains a nullable `BoundingBox` from the mark's own
  polygon.
- `AnalyzeResultMapper` builds a page-number → `(Width, Height)` lookup from
  `result.Pages` once per call, then normalizes each region against it. Verify
  the exact SDK member types against `Azure.AI.DocumentIntelligence` 1.0.0
  during implementation (`BoundingRegion.Polygon`, `DocumentPage.Width`,
  `DocumentPage.Height`, `DocumentSelectionMark.Polygon`).

**Normalization**

- `DocumentField` carries both boxes through `DocumentNormalizationService`,
  for key/value pairs and for selection marks alike.

**Rules**

- `ValidationRuleBase.Result` accepts an optional `BoundingBox`.
- `ValidationResult` and `ValidationReportItem` carry **one resolved box**, so
  the frontend never chooses.
- `RequiredFieldRule` resolves it:

  | Case (`RequiredFieldRule.ValidateAsync`) | Box |
  |---|---|
  | present, has a value | `ValueBoundingBox ?? KeyBoundingBox` |
  | present but empty | `KeyBoundingBox ?? ValueBoundingBox` |
  | `match is null` | `null` |

  The empty case prefers the **key** region deliberately: the printed label is
  what a reviewer needs to find, and the value region of a blank field is
  absent or too small to see.

- `CheckboxRule` and `SignatureRule` attach the selection mark's box when the
  mark was found. When OCR found nothing, the box stays `null` — no invented
  regions.

**Persistence**

- `BoundingBox` maps as an EF owned type nested inside the existing owned
  collections — two instances per `DocumentField` (key and value) and one per
  `ValidationReportItem` — following the `OwnsMany` style already in
  `NormalizedDocumentConfiguration` and `ValidationReportConfiguration`.
- Owned types flatten to **columns on the existing tables**, not new tables:
  `NormalizedDocumentFields` gains eight columns
  (`KeyBoundingBox_X` … `ValueBoundingBox_Height`) and
  `ValidationReportItems` gains four.
- One EF migration. All columns nullable.
- Documents normalized before this change keep null boxes until re-extracted.
  The UI already handles null through the no-region message, so **no backfill
  job ships in v1**.

**API**

- `ValidationReportDto`'s item gains a nullable bounding box.
- `validation.model.ts` mirrors it on `ValidationReportItem`.

**Tests**

- `AnalyzeResultMapperTests`: polygon to normalized box; inch and pixel pages
  both normalize identically; polygon with fewer than eight values yields null;
  missing or zero page dimensions yield null; out-of-range values clamp;
  rotated quadrilateral yields a correct axis-aligned box.
- Normalization: both boxes survive to `DocumentField`; selection-mark box
  survives.
- `RequiredFieldRule`: all three rows of the table above.
- `CheckboxRule`, `SignatureRule`: box present when the mark was found, null
  when it was not.
- Integration: EF round-trip of both owned boxes, including all-null.
- `dotnet build` and `dotnet test` green.

### PR-B — pdf.js viewer and two-column layout

**pdf.js integration — the main risk, spike first**

Add `pdfjs-dist`. The unknown is worker wiring under Angular 22's build:
`GlobalWorkerOptions.workerSrc` must resolve to a bundled
`pdf.worker.min.mjs`, which is where this can consume time. Options, in order
of preference: a `new Worker(new URL(...), { type: 'module' })` reference the
bundler can follow; failing that, an `assets`/`public` copy of the worker file
declared in `angular.json`. **Spike this before the rest of PR-B**, and let it
inform whether the viewer keeps a fallback path. It does not block PR-A.

**Viewer**

- `DocumentViewer` renders the target page to a `<canvas>` sized to its
  container at `devicePixelRatio`, replacing the `<iframe>` and its
  `#page=` fragment hint.
- A sibling overlay `<div>`, sized to the canvas's CSS box, holds one
  absolutely positioned `<div>` per box, positioned with **percentage**
  `left/top/width/height`. Percentages mean resize needs no recomputation;
  only the canvas re-renders, debounced via `ResizeObserver`, to stay crisp.
- Divs rather than a canvas overlay: they take CSS transitions, can be
  focusable for keyboard and screen-reader users, and need no redraw loop.
  (OpenEMR's plan specified divs; its shipped partial drifted to canvas.)
- Existing `ViewerState` handling — `external`, `unavailable`, `error`,
  and their messages — is preserved.

**Validation report**

- `validation-report.html` becomes a two-column grid: findings left, viewer
  sticky right, collapsing to stacked below 1100px.
- `ValidationReportPanel` computes the box set for the open document and page
  per the [draw policy](#draw-policy) and passes it to the viewer.
- A finding with `documentId` but no region shows **"Couldn't locate this field
  on the page"** inline, and the viewer repeats it against the rendered page.

**Tests**

- Overlay renders one div per box with correct percentage geometry.
- Only issue findings for the open document and page are drawn; Complete
  findings are not.
- Active versus inactive styling; second click clears the active state.
- Zero-box open (the citation path) renders the page cleanly.
- No-region finding renders the message and no box.
- pdf.js mocked; no real PDF parsing in unit tests.
- `npm test` and `npm run build` green.

## Risks

| Risk | Mitigation |
|---|---|
| pdf.js worker bundling under Angular 22 | Spike first in PR-B; PR-A unaffected |
| Key and value regions on different pages | Page follows the chosen box |
| Pre-existing normalized documents have no boxes | Null-safe UI; re-extract to populate; no backfill in v1 |
| Layout cramped on narrow screens | Stack below 1100px |
| pdf.js bundle size on first viewer load | Lazy-load the viewer route; measure in PR-B |

## Open questions

None blocking. Deferred by choice: whether click-to-highlight should also work
for Complete findings in a later pass — the data is persisted for it, but no UI
ships in v1.
