# MVP End-to-End Test Plan

How the MVP is verified as a whole, what the end-to-end suite proves, and — set
out plainly in [Known limitations](#known-limitations) — what it does not.

See [`system-overview.md`](../architecture/system-overview.md) for the
architecture, [`rag-architecture.md`](../architecture/rag-architecture.md) for
retrieval and corpus separation, and [`security.md`](../architecture/security.md)
for the injection defenses this suite exercises through HTTP.

## The Scenario

One reviewer working one floodplain development permit case, from an empty
system to a cited answer:

```text
Create case
  → Upload application documents
  → Store in Blob
  → Extract
  → Normalize
  → Validate
  → Index
  → View validation report
  → Ask case question
  → Ask county question
  → Compare submission against requirement
  → Verify citations
```

## Where the Tests Live

| Suite | Location | What it covers |
|---|---|---|
| Backend end-to-end | `backend/tests/HarrisCountyAI.IntegrationTests/EndToEnd/` | The scenario above, over real HTTP, SQL Server, and blob storage |
| Backend integration | `backend/tests/HarrisCountyAI.IntegrationTests/` | Per-endpoint and per-adapter behavior |
| Backend unit | `backend/tests/HarrisCountyAI.UnitTests/` | Rules, services, prompts, sanitization |
| Backend architecture | `backend/tests/HarrisCountyAI.ArchitectureTests/` | Clean Architecture layer dependencies |
| Frontend | `frontend/src/app/**/*.spec.ts` | Components, services, and one client-side journey (`reviewer-workflow.spec.ts`) |

## How the End-to-End Suite Runs

**Everything runs offline.** No test in this suite reaches an Azure endpoint,
and none needs a credential. The four external AI dependencies are replaced at
the narrowest seam each offers, so everything above them is production code:

| Dependency | Replaced by | Seam |
|---|---|---|
| Azure AI Document Intelligence | `ScriptedExtractionService` | `IDocumentExtractionService` |
| Azure OpenAI embeddings | `StubEmbeddingService` | `IEmbeddingService` |
| Azure OpenAI chat | `ScriptedLanguageModelService` | `ILanguageModelService` |
| Azure AI Search | `InMemorySearchIndex` | `ISearchIndexGateway`, `ISearchQueryGateway` |

Replacing Azure AI Search at the *gateway* rather than at `IRetrievalService`
matters: the real `AzureDocumentIndexService` and `AzureRetrievalService` run,
including the mandatory scope filters that keep the reference corpus and case
documents apart. The corpus separation these tests assert is therefore the
production behavior, not a fixture's arrangement.

What is real: the HTTP pipeline including authentication and authorization, SQL
Server persistence and migrations, Azurite blob storage, document normalization
and field classification, chunking, indexing, retrieval, every validation rule,
the prompt builders, and the sanitization boundary.

### Running it

```bash
docker compose up -d          # SQL Server on 1433, Azurite on 10000
cd backend && dotnet test     # the whole suite, including end to end
cd frontend && npx ng test --watch=false
```

The only tests that touch a real Azure service are the two live Azure AI Search
tests in `Search/AzureDocumentIndexServiceTests`, marked `[AzureSearchFact]` and
skipped unless `SEARCH_ENDPOINT` and `SEARCH_API_KEY` are present in the
environment. That is deliberate: the project runs on a fixed Azure credit
budget, and a test suite must never spend it.

## Checklist Coverage

Each item from the PR-44 checklist, and the tests that carry it.

| Checklist item | Covered by |
|---|---|
| Valid application | `ReviewWorkflowEndToEndTests.A_Valid_Submission_Passes_Every_Requirement_Without_Calling_The_Model`, `.An_Uploaded_Document_Is_Stored_Extracted_Normalized_And_Indexed` |
| Incomplete application | `ReviewWorkflowEndToEndTests.An_Incomplete_Submission_Reports_What_Is_Missing_And_What_Needs_A_Human`, `.Values_That_Fail_Their_Rules_Are_Reported_Invalid_Rather_Than_Missing` |
| Malformed document | `ReviewWorkflowEndToEndTests.A_Malformed_Document_Fails_Processing_Without_Blocking_The_Rest_Of_The_Case` |
| Semantic validation | `SemanticValidationEndToEndTests` (6 tests: verdicts, outage, malformed verdict, and the deterministic gates that keep the model out of it) |
| County question | `QuestionAnsweringEndToEndTests.A_County_Question_Is_Answered_From_The_Ingested_Reference_Corpus` |
| Case question | `QuestionAnsweringEndToEndTests.A_Case_Question_Is_Answered_From_That_Cases_Own_Documents` |
| Dual-source question | `QuestionAnsweringEndToEndTests.A_Comparison_Reads_Both_Corpora_And_Tags_Every_Citation_With_Its_Source`, `.A_Comparison_With_Only_One_Side_Refuses_To_Answer` |
| Citation navigation | `QuestionAnsweringEndToEndTests.A_Case_Citation_Opens_The_Document_It_Points_At`, `ReviewWorkflowEndToEndTests.A_Validation_Item_Points_At_The_Document_And_Page_A_Reviewer_Can_Open` |
| Insufficient evidence | `QuestionAnsweringEndToEndTests.A_County_Question_Never_Retrieves_A_Cases_Uploaded_Documents`, `.A_Case_Question_With_Nothing_Indexed_Reports_Insufficient_Evidence`, `.An_Answer_That_Cites_Nothing_Is_Not_Presented_As_An_Answer` |
| Prompt injection | `PromptInjectionEndToEndTests` (13 tests across the seven adversarial documents) |
| Case isolation | `CaseIsolationEndToEndTests` — **partially; see the gap below** |
| Known limitations | This document |

### What the injection tests add over the unit suite

The unit suite (`HarrisCountyAI.UnitTests/Security/`) proves `UntrustedText` and
the prompt builders hold their invariants when handed a payload directly. The
end-to-end tests upload the *same* adversarial documents — linked from the unit
project so a newly added attack is covered in both — and drive them through
upload, extraction, normalization, chunking, indexing, and retrieval before
anything reaches a prompt. Each asserts a distinctive phrase from the payload
actually arrived in the prompt, so the test cannot pass because the document
never got there.

They also cover a case the unit tests structurally cannot: what happens when the
model *complies* with an injected instruction. A model that returns an
approval with no citations is downgraded to insufficient evidence, so obedience
to an injected document still cannot produce an ungrounded answer.

## Known Limitations

Recorded as they are, not as they should be.

### 1. Case access is authorized by role only — there is no per-case ownership

**This is the significant one.** The MVP has no case assignment, no owning
reviewer, and no per-case access check anywhere in the request path. Any caller
holding a valid `Reviewer` token can read any case, its documents, its stored
files, and its validation reports.

The suite documents this rather than papering over it:
`CaseIsolationEndToEndTests.Any_Reviewer_Can_Open_Any_Case_Today_Because_Cases_Have_No_Owner`
asserts that a second, unrelated Reviewer identity gets `200 OK` on all three.
When per-case authorization lands, that test should be replaced with one
asserting `403`; its failure at that point is the intended signal.

What *does* hold, and is asserted, is **evidence isolation**:

- Retrieval carries a mandatory case filter, so a case-scoped question can never
  return another case's passages, and a county question can never return case
  documents at all.
- Every case-scoped route resolves child resources within the case in the URL,
  so a document or report id belonging to another case is a `404`, not a leak.
- The role boundary that exists is enforced: anonymous callers get `401`, and a
  Reviewer reaching a knowledge-base endpoint gets `403`.

So the current guarantee is "one case's evidence never bleeds into another's",
not "only the assigned reviewer can open this case".

### 2. The extraction pipeline runs synchronously, with no background worker

`IDocumentProcessingService` — extract, normalize, persist, index — is driven
by `POST /api/cases/{caseId}/documents/{documentId}/process`, the endpoint the
reviewer's browser calls after an upload completes. The end-to-end suite drives
it the same way (`EndToEndTestBase.ProcessAsync`); every step in the scenario
goes over the wire, and nothing reaches into the host's service provider to
move the workflow along.

What remains is that the run is **synchronous**: the HTTP response arrives only
when the pipeline finishes, so a large document holds a request open for as
long as Document Intelligence and the embedding calls take, and a client that
disconnects mid-run gets no outcome (the document is still recorded `Failed`,
so it is never left stuck mid-run). There is no queue, no background worker,
and no polling status endpoint. A run that fails answers `200 OK` carrying the
document's terminal `Failed` status and the reason — deliberately not a 5xx, so
a caller can tell "the pipeline ran and failed" from "the call never landed",
which are different things to retry. Processing is also not guarded against two
callers processing the same document at once; the last run wins, and because
indexing is delete-then-index the index does not accumulate duplicates.

Moving this to a background job would be a much larger change — queue
infrastructure, a worker host, and a status-polling contract — and is out of
scope for the MVP.

### 3. The frontend has no browser end-to-end harness

No Playwright, Cypress, or WebDriver setup exists, and adding one would mean a
browser download and a new toolchain for a project that does not otherwise have
either. Frontend behavior is covered by the Angular component and service specs,
plus `frontend/src/app/reviewer-workflow.spec.ts`, which walks the whole reviewer
journey across the real services against a mocked HTTP backend and asserts every
request's method, URL, body shape, and bearer token. That catches client/server
contract drift; it does not catch rendering or navigation defects a browser
would.

### 4. Retrieval relevance is not exercised end to end

The stub embedding service returns a fixed vector, and the in-memory index
returns every chunk its scope filter admits, in insertion order. The end-to-end
tests therefore prove that the right *set* of evidence is reachable and the wrong
set is not — they prove nothing about ranking quality. Relevance is a separate
concern, measured by the retrieval evaluation dataset under `evaluation/`.

### 5. Model behavior is scripted, not observed

Every model response in the suite is one the test wrote. That is the right
boundary for testing our code — it makes the fail-closed paths (uncited answers,
malformed verdicts, unrecognized statuses, outages) directly assertable — but it
means these tests say nothing about how a real deployment answers a real
question, or about whether a real model resists an injected instruction. Our
defenses are structural for exactly that reason: they do not depend on the model
choosing correctly.

### 6. The dual-source comparison is backend-only

`scope: "Both"` works end to end through the API and is tested here, but the
Angular client's `QuestionScope` type admits only `County` and `Case`. A
reviewer cannot ask for a comparison from the UI yet.

### 7. Not covered

- Concurrency: two reviewers editing one case, or overlapping validation runs.
- Volume: large PDFs, many-page documents, or a corpus at realistic scale.
- The Entra ID authentication mode (only local-development JWT is exercised).
- Live Azure behavior: throttling, transient failures, and real service latency.
