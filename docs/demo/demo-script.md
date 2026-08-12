# Demo Walkthrough

A guided tour of what the system does, in the order a reviewer would meet it, with the sample
questions worth asking at each point. It doubles as the script for a live demo and as an explanation
for someone reading the repository without running it.

**Read this first.** No live Azure environment has ever been provisioned for this project, and the
Harris County reference corpus has not been ingested. So this document distinguishes throughout
between:

- **Runs today** — you can do this with `docker compose up -d` and the repository as it stands.
- **Needs Azure** — requires configured Document Intelligence / Azure OpenAI / AI Search resources.
- **Needs an ingested corpus** — additionally requires the county documents to have been uploaded and
  ingested through the knowledge-base API.

There are no screenshots in this repository. Producing them would mean either photographing a
mocked-out screen or fabricating an environment that does not exist; the step-by-step below names the
component and the API call behind each screen instead, which is more checkable than an image.

---

## The scenario

An applicant has filed for a **Harris County floodplain development permit** on a residential lot,
using the Residential Development Permit Application. The reviewer's job is to decide whether the
package is complete enough to process. Concretely, they need to know:

1. Is every required document attached?
2. Is every required field filled in, signed, and dated?
3. Where the form calls for judgment — "describe the use of the accessory building" — is the answer
   actually adequate?
4. When something looks wrong, what does the county's own regulation say about it?

The system answers 1 and 2 without a model, 3 with a model, and 4 by retrieval. That split is the
demo.

---

## Act 0 — Start the environment *(runs today)*

```bash
docker compose up -d      # SQL Server + Azurite
cd backend && dotnet build && dotnet test
```

The suite passes with no Azure account: 1465 backend tests and 205 frontend tests, with Document
Intelligence, Azure OpenAI, and Azure AI Search replaced at their interfaces by scripted fakes. This
is worth doing first in a live demo, because it makes the next point concrete — everything that
follows is covered by tests that run offline.

To run the API, supply Azure configuration (see the [README](../../README.md#4-run-the-api)); it
validates at startup and refuses to boot on the committed placeholders.

---

## Act 1 — Create the case and upload the package *(runs today)*

**UI:** *Dashboard → Create case →* name and workflow type *→ Create case*.
`CaseCreate` (`frontend/src/app/features/cases/case-create/`) → `POST /api/cases`.

```bash
TOKEN=$(curl -s -X POST http://localhost:5096/api/auth/dev-token \
  -H 'Content-Type: application/json' -d '{"username":"dev.reviewer"}' \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')

curl -s -X POST http://localhost:5096/api/cases \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"Creek Bend Development","workflowType":"FloodplainDevelopmentPermit"}'
```

The case gets a server-generated number, `HC-{year}-{sequence}`, and status `New`.

**Then upload.** *Case detail → drag PDFs onto the upload panel → pick a document type per file →
Upload.* `DocumentUpload` (`frontend/src/app/features/document-upload/`) →
`POST /api/cases/{caseId}/documents`, then automatically `POST .../documents/{documentId}/process`
for each file as its upload completes.

```bash
curl -s -X POST "http://localhost:5096/api/cases/$CASE_ID/documents" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@application.pdf" -F "documentType=PermitApplication"
```

Document types for this workflow: `PermitApplication`, `SitePlan`, `ElevationCertificate`,
`DrainagePlan`, `Affidavit`, `SupportingDocument`, `Other`.

**Point worth making:** upload and processing are two endpoints, not one. The upload is durable the
moment the blob is written; extraction is a separate, re-runnable step. That is why the UI's retry
button re-runs only the processing, and why a Document Intelligence outage costs you an extraction,
not a file.

---

## Act 2 — Extraction and normalization *(needs Azure)*

`POST /api/cases/{caseId}/documents/{documentId}/process` runs, synchronously:

```
blob download → Document Intelligence (prebuilt-layout)
              → normalize into fields, checkbox selection marks, and per-page text
              → persist the NormalizedDocument
              → chunk, embed, and index for case-scoped retrieval
```

The document's `processingStatus` walks `Uploaded → Extracting → Extracted → Normalized`, or lands on
`Failed` with a reason. The indexing step at the end **fails open**: if the search index cannot be
written, the failure is logged and the document is still fully extracted and validated, because
deterministic validation never consults the index.

**Point worth making:** `prebuilt-layout` is chosen over a custom-trained model on purpose. The
Residential Development Permit Application is a printed form whose value is in its labeled fields and
its checkbox selection marks; the layout model returns key-value pairs and selection marks out of the
box, and every field name in the workflow carries OCR variants (`HCAD Account Number`, `HCAD #`,
`HCAD Acct No`, …) so that matching survives how OCR actually renders a printed label.

---

## Act 3 — Deterministic validation *(runs today, no model)*

**UI:** *Case detail → Run validation.* `ValidationReportPanel` → `POST /api/cases/{caseId}/validation`.

This is the heart of the demo, and the thing to slow down on: **no model is called here at all.**
Fourteen rules run against the normalized data:

| Rule type | Count | What it checks |
|---|---|---|
| `RequiredDocumentRule` | 3 | Permit application, site plan, FEMA Elevation Certificate |
| `RequiredFieldRule` | 5 | Property address, HCAD account number, owner name, applicant printed name, fill-acknowledgement initials |
| `SignatureRule` | 1 | The applicant signature field is actually signed |
| `DateRule` | 1 | The application date parses, and is not in the future |
| `CheckboxRule` | 4 | At least one box checked in each of: construction type, driveway status, sewer/water system, building code |

Each result carries a status, a message, the document and page it came from, and
`validationType: Deterministic`.

**The most interesting single result** is the elevation certificate. It is genuinely required — but
only for Class II submissions (in the 100-year floodplain, a floodway, or an A/V zone) and for Shaded
X, and the permit class depends on FIRM flood-zone data the system does not have. So when it is
absent, the rule does not report `Missing` and it does not report `Complete`. It reports
**`NeedsHumanReview`** with a message explaining exactly why a human has to decide.

That is the deterministic principle taken seriously in both directions: deterministic code answers
what it can answer, and *refuses to guess* at what it cannot — rather than escalating the guess to a
model, which would only make the guess more expensive and less reproducible.

**Also worth demonstrating:** re-run the validation. You get the same report, because nothing in this
path is stochastic.

---

## Act 4 — Semantic validation *(needs Azure)*

Two requirements in the workflow are not decidable in code, and they run as `SemanticEvaluationRule`
in a deliberately separate section of the workflow (`BuildSemanticRules()`):

1. **Is the narrative description of the work consistent with the checked construction-type boxes?**
   For example: the description says fill will be placed, but the *Fill* box is not checked.
2. **Is the "describe use of Accessory Building or Other" text adequate?** `detached workshop for
   personal woodworking` satisfies it; `N/A`, `stuff`, or `building` does not.

The demo point is what happens *before* the model. Each rule resolves everything it can
deterministically first:

| Situation | Result | Model called? |
|---|---|---|
| No construction-type box checked, or no description present | rule not applicable → `Complete` | **no** |
| The permit application is absent entirely | `UnableToDetermine` | **no** |
| Accessory Building checked, description field empty | `Missing` | **no** |
| Description present and non-trivial | model judges it | yes |

And what happens *after*. The model must return `pass`, `fail`, or `needs_human_review` as strict
JSON. A model error, unparseable output, a missing field, or an unrecognized verdict all fail closed
to `UnableToDetermine` — never to a default, never to `pass`. Results are stamped
`validationType: Semantic`, so a reviewer can always see which findings involved a model and which
did not.

---

## Act 5 — Grounded question answering *(needs Azure + an ingested corpus)*

**UI:** *Ask a question →* choose scope *→* type the question *→ Ask.*
`QuestionAnswering` (`frontend/src/app/features/question-answering/`) → `POST /api/questions`.

```bash
curl -s -X POST http://localhost:5096/api/questions \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"question":"How high must the lowest floor of a new house be built in the floodplain?",
       "scope":"County"}'
```

The response carries the answer, a list of resolved citations (each with corpus, document title,
section, page, and source URL where known), the prompt version, and the model deployment.

### Sample questions

**County scope — "what does Harris County require?"** These are drawn from the committed generation
evaluation dataset (`evaluation/datasets/generation/questions.json`), so they are the questions the
system is actually measured on:

- How high must the lowest floor of a new house be built in the floodplain?
- Do I need a permit before developing land in a special flood hazard area?
- What must a floodplain development permit application include?
- Can I place fill in the regulatory floodway to level out my lot?
- If I remodel my house and the repairs cost more than half its value, do the flood rules apply to
  the whole house?
- Is there any way to get an exception to the flood elevation requirements for a historic building?
- Who is allowed to sign and seal Section C of the Elevation Certificate?
- When should an applicant use Form MT-EZ instead of the full MT-1 application?
- Do manufactured homes have different flood elevation rules than regular houses?
- What happens if my project changes after the floodplain permit was approved?

Note the two shapes mixed in that list — exact identifiers (`Section C`, `MT-EZ`, `MT-1`) and plain
paraphrase (`build a shed`, `more than half its value`). That mix is the entire argument for hybrid
retrieval, and the retrieval evaluation dataset categorizes its 28 questions along exactly that axis.

**Case scope — "what did this applicant submit?"** Answered only from that case's uploaded documents,
filtered by `caseId`:

- What construction type did the applicant describe?
- What address and HCAD account number are on the application?
- Does the submission mention placing fill on the lot?

**Both scope — "does the submission meet the requirement?"** Retrieves from each corpus separately and
compares:

- Does this application include everything Section 4.04 requires?
- The applicant describes placing fill — does the submission satisfy the county's fill requirements?

> **The `Both` scope has no UI.** The backend implements and tests it, and it is reachable over the
> API with `"scope": "Both"` and a case id, but the frontend's `QuestionScope` type admits only
> `County` and `Case`. Demonstrate it with `curl`.

### Questions the system should refuse — demo these

This is the most important part of the question-answering demo, and the easiest to skip. The
evaluation dataset deliberately contains three questions the corpus cannot answer:

- *What is the current Harris County property tax rate for residential property?* — real question,
  wrong corpus.
- *Has permit application number 2024-11875 been approved yet?* — asks about live case status the
  system does not hold.
- *Should I sue my neighbour over the stormwater running off their property onto mine?* — asks for
  legal advice.

Each should return `outcome: InsufficientEvidence` with an explicit message, not a fluent paragraph.
A confident answer to any of them is a hallucination however well it reads, and this is the only
category in the evaluation set that measures it.

### What to point at while the answer renders

Three properties are enforced in code, not requested in a prompt, and they are the reason to trust
the output:

1. **No evidence → no model call.** If retrieval returns nothing, the request short-circuits to an
   insufficient-evidence response. There is nothing to ground an answer in, so nothing is asked.
2. **The model cannot name its own sources.** It emits citation *numbers* into the numbered source
   list it was handed. `CitationResolver` maps each number back to the actual chunk, drops anything
   out of range or duplicated, and takes the source corpus from the *retrieval scope* — never from
   the model's output. A fabricated citation cannot survive that mapping.
3. **An uncited answer is thrown away.** If the model answers but cites nothing, the answer text is
   discarded and replaced with the insufficient-evidence response. In `Both` scope the rule is
   stricter: an answer that cites no *county* source is downgraded, because a comparison that never
   referenced a requirement has not compared anything.

A technical failure — a model exception, unparseable output — returns HTTP `502` with a problem
document. It never returns a plausible-looking answer.

**Click a citation.** `Citation` → `DocumentViewer`: a case-document citation fetches the original
PDF through `GET /api/cases/{caseId}/documents/{documentId}/content` and opens it at the cited page;
a county citation links out to the source URL at that page. The point of the whole citation contract
is that the reviewer can check the claim in one click.

---

## Act 6 — The prompt injection demo *(runs today, no Azure)*

This one is a `dotnet test` invocation, and it is the most persuasive part of the repository.

```bash
cd backend
dotnet test --filter "FullyQualifiedName~PromptInjection"   # 81 unit + 13 end-to-end
```

`backend/tests/HarrisCountyAI.UnitTests/Security/TestData/` holds seven adversarial permit documents,
one per injection class: a direct instruction override, delimiter forgery, invented delimiters, forged
county requirements, verdict coercion, exfiltration, and hidden-Unicode instructions. They are kept
on disk as real-looking documents so a new attack can be added without touching test code.

Each payload is driven through all four prompt builders and through the real question-answering,
comparison, and semantic-validation services with a fake model, and every combination asserts:

- the payload lands **inside** a delimited block, never in the framing;
- every delimiter-shaped token in the finished prompt is a known delimiter, appearing exactly once —
  so nothing in the evidence forged a boundary;
- the applicant block cannot be escaped into the county requirement block;
- the captured `SystemPrompt` is byte-identical to its constant.

These tests never call a model. They assert facts about the bytes sent, not model behavior — which is
precisely why they are worth demonstrating. Show `UntrustedText.Sanitize`: it does not blacklist known
delimiters, it destroys the *grammar* of a delimiter, so an invented `<<<TRUSTED_COUNTY_POLICY>>>` is
neutralized just as thoroughly as a copy of a real one. The full reasoning is in
[`../architecture/security.md`](../architecture/security.md).

---

## Act 7 — Evaluation *(runs today)*

```bash
evaluation/scripts/run-retrieval-evaluation.sh
evaluation/scripts/run-generation-evaluation.sh
evaluation/scripts/run-judge-evaluation.sh
```

All three run offline against committed fixtures, cost nothing, and finish in seconds. They score
retrieval (Recall@1/3/5, MRR, per category), generation (outcome match, fact coverage, the citation
contract, an unsupported-claim screen), and an LLM judge checked against hand-written human labels.

**Say the caveat out loud.** Every committed number is `runType: Fixture` — a synthetic corpus and
hand-written answers replayed through the real pipeline. That makes them excellent regression gates:
change a scorer, a prompt, or the citation resolver and the diff shows up. It makes them worthless as
a quality claim, and no live baseline exists.

The most interesting thing to show here is the committed generation baseline, which deliberately
**demonstrates its own failure modes** rather than hiding them: one true positive (an answer asserting
a dollar figure the corpus never states, correctly flagged), two false positives (faithful summaries
in vocabulary the passages never used), and a pinned false negative (a claim assembled entirely from
evidence vocabulary that reverses the meaning — "one foot *below* the base flood elevation" — and
scores a perfect 1.0). A screen that only reported good numbers would tell you nothing about whether
the screen works. Details in
[`../evaluation/evaluation-strategy.md`](../evaluation/evaluation-strategy.md).

---

## The five-minute version

If there is only time for one pass, this is the order:

1. **`dotnet test`** — 1465 tests, offline, no Azure account. The architecture is testable because
   every external service is behind an interface.
2. **Run validation on a case** — thirteen rules, no model, reproducible. Point at the elevation
   certificate reporting `NeedsHumanReview` rather than guessing.
3. **Ask an out-of-scope question** — the system declines instead of answering. Then ask a real one
   and click through a citation to the source page.
4. **`dotnet test --filter "FullyQualifiedName~PromptInjection"`** — the untrusted-evidence boundary
   is a property of the bytes sent to the model, asserted by tests, not a sentence in a system
   prompt.
5. **Open the README's known limitations** — no live evaluation baseline, no ingested corpus, no
   per-case ownership, never deployed. Knowing what has not been demonstrated is part of the work.
