# API Endpoints

Base URL in local development: `http://localhost:5096` (the `http` launch profile) or
`https://localhost:7074` (the `https` profile). See
`backend/src/HarrisCountyAI.Api/Properties/launchSettings.json`.

In Development the API also serves an OpenAPI document at `/openapi/v1.json` and Swagger UI at
`/swagger`, both anonymous. That is the authoritative, always-current reference; this page is the
narrative one.

## Conventions

- Enums serialize as strings; timestamps are UTC ISO-8601.
- Errors are RFC 7807 `application/problem+json`, and every response carries an `X-Correlation-Id`
  header (echoed from the request when it is well formed, generated otherwise).
- A dependency that is unreachable produces `503` with a `service` field naming it, never a stack
  trace.
- The API registers a CORS policy **only in the Development environment**, admitting the origins in
  `Cors:AllowedOrigins` (`http://localhost:4200` by default) so the Angular dev server can reach it.
  `X-Correlation-Id` is exposed to the browser; credentials are not allowed. In Azure no policy is
  registered — the frontend origin is allowed at the App Service layer by the deployment workflow.

## Authorization

Every endpoint requires a bearer token unless marked anonymous. Two role policies are in use.

| Route prefix | Policy |
|---|---|
| `/api/cases`, `/api/cases/{id}/documents`, `/api/cases/{id}/validation`, `/api/questions` | `Reviewer` |
| `/api/knowledge-base`, `/api/debug/retrieval` | `Administrator` |
| `/api/auth/dev-token`, `/health` | anonymous |

There is no per-case authorization: any authenticated Reviewer can reach any case.

---

## Health

### `GET /health`

Anonymous. Returns `200` with the body `Healthy` when the process is running. It does **not** probe
SQL, Blob Storage, Search, or the model — a dependency that is down still returns `Healthy`.

---

## Auth

### `POST /api/auth/dev-token`

Anonymous, and registered **only** when `Authentication:Mode` is `LocalDevelopment`; in any other
mode the route returns `404`.

```json
{ "username": "dev.reviewer" }
```

| Response | When |
|---|---|
| `200` — `{ accessToken, tokenType, expiresAt, username, displayName, roles }` | The username is in the development allow list |
| `400` | Username missing, blank, or not in the allow list |
| `404` | The API is not in `LocalDevelopment` mode |

Allow-listed users are configured in `appsettings.Development.json`: `dev.reviewer` (`Reviewer`) and
`dev.admin` (`Administrator`).

---

## Cases

```json
{
  "id": "8f7f9d3e-2f2a-4f0e-9c93-1c9d3c5b6a01",
  "caseNumber": "HC-2026-0001",
  "name": "Creek Bend Development",
  "workflowType": "FloodplainDevelopmentPermit",
  "status": "New",
  "createdAt": "2026-08-11T19:04:32.1234567Z",
  "updatedAt": "2026-08-11T19:04:32.1234567Z"
}
```

- `workflowType`: `FloodplainDevelopmentPermit`
- `status`: `New`, `Processing`, `ReadyForReview`, `InReview`, `Completed`
- `caseNumber` is server-generated per year: `HC-{year}-{sequence:0000}`.

| Endpoint | Behavior |
|---|---|
| `POST /api/cases` | Body `{ name, workflowType }`. `201` with a `Location` header; `400` if `name` is blank or `workflowType` is unknown. |
| `GET /api/cases` | `200` with all cases, newest first. |
| `GET /api/cases/{id}` | `200`, or `404`. |
| `PATCH /api/cases/{id}` | Body `{ name?, status? }`; omitted fields are unchanged. `200`, `400` on a blank name or unknown status, `404` if absent. |

---

## Case documents

Routed under `/api/cases/{caseId:guid}/documents`.

```json
{
  "id": "…", "caseId": "…", "fileName": "application.pdf",
  "documentType": "PermitApplication", "processingStatus": "Normalized",
  "createdAt": "2026-08-11T19:06:00.0000000Z"
}
```

- `documentType`: `PermitApplication`, `SitePlan`, `ElevationCertificate`, `DrainagePlan`,
  `Affidavit`, `SupportingDocument`, `Other`
- `processingStatus`: `Pending`, `Uploaded`, `Extracting`, `Extracted`, `Normalized`, `Failed`

### `POST /api/cases/{caseId}/documents`

`multipart/form-data` with `file` and `documentType`. `201` with the created document, `400` on a
validation failure, `404` if the case does not exist.

Accepted: extensions `.pdf .png .jpg .jpeg .tif .tiff`, content types `application/pdf`,
`image/png`, `image/jpeg`, `image/tiff`, size between 1 byte and 50 MB. All rules are evaluated, so a
rejected upload reports every reason at once. The declared content type is trusted — file contents
are not sniffed, and nothing scans for malware.

### `POST /api/cases/{caseId}/documents/{documentId}/process`

Runs extraction, normalization, and search indexing **synchronously**; the response arrives when the
pipeline finishes. `200` with `{ document, failureReason? }`, `404` if the case or document is
absent.

A pipeline failure is reported in the body with the document's status set to `Failed`, not as an HTTP
error — the request itself succeeded in attempting the work. Re-running is safe and is the intended
retry path; indexing is delete-then-index, so no duplicate chunks accumulate.

### `GET /api/cases/{caseId}/documents` · `GET …/{documentId}`

`200` with the document list or a single document; `404` if the case or document is absent.

### `GET /api/cases/{caseId}/documents/{documentId}/content`

Streams the original file back with its content type, so the UI can open a cited page in a viewer.
`404` if absent.

---

## Validation

Routed under `/api/cases/{caseId:guid}/validation`.

| Endpoint | Behavior |
|---|---|
| `POST /api/cases/{caseId}/validation` | Runs the case's workflow rules and persists a report. `201` with the report; `404` if the case is absent. |
| `GET /api/cases/{caseId}/validation` | `200` with the **latest** report; `404` if none has been run. |
| `GET /api/cases/{caseId}/validation/{reportId}` | `200` with that specific report; `404` if absent. |

Each report item carries `ruleName`, `requirement`, `validationType` (`Deterministic` or `Semantic`),
`status`, `message`, and — where known — `extractedValue`, `documentId`, `documentType`, and
`pageNumber`.

`status` is one of `Complete`, `Missing`, `Invalid`, `PotentiallyIncomplete`, `NeedsHumanReview`,
`UnableToDetermine`. The last two are load-bearing rather than filler: `NeedsHumanReview` means the
rule identified a decision it must not make, and `UnableToDetermine` means a check could not run to a
conclusion. Neither is ever silently coerced into a pass.

---

## Questions

### `POST /api/questions`

```json
{ "question": "What must a floodplain permit application include?", "scope": "County", "caseId": null }
```

- `question` — required, at most 1000 characters.
- `scope` — `County` (default), `Case`, or `Both`; parsed case-insensitively.
- `caseId` — required for `Case` and `Both`, ignored for `County`.

| Response | When |
|---|---|
| `200` — answer payload | Answered, or an explicit insufficient-evidence result |
| `400` | Missing/overlong question, unknown scope, or a scope that needs a case id without one |
| `502` | The question could not be answered for a technical reason (model error, unparseable output) |

`County` and `Case` return `{ outcome, answer, citations, promptVersion, modelDeployment }`. `Both`
returns the same fields plus `countyEvidenceCount` and `caseEvidenceCount`.

`outcome` is `Answered`, `InsufficientEvidence`, or `Failed`. Each citation carries
`{ number, source, chunkId, documentId, title, section, page, sourceUrl }`, where `source` is the
corpus the chunk came from — assigned from the retrieval scope in code, never taken from the model's
output.

An answer that cites nothing is downgraded to `InsufficientEvidence` and its text discarded; in
`Both` scope, so is an answer that cites no county source.

---

## Knowledge base

Administrator only. Routed under `/api/knowledge-base`.

| Endpoint | Behavior |
|---|---|
| `POST /api/knowledge-base/documents` | `multipart/form-data`: `file`, `title`, `department`, `permitType`, `documentType`, and optional `version`, `effectiveDate`, `sourceUrl`. `201`; `400` on a validation failure or an unparseable date. |
| `GET /api/knowledge-base/documents?includeDeactivated=true` | `200` with the corpus documents; deactivated ones are excluded unless `includeDeactivated` is set. |
| `POST /api/knowledge-base/documents/{id}/ingest` | Extract → chunk → embed → index, synchronously. `200` with `{ documentId, status, chunkCount, failureReason }` where `status` is `Succeeded` or `Failed`; `404` if absent; `409` if the document is already `Processing` or has been deactivated. Also the re-index path after a corpus update. |
| `DELETE /api/knowledge-base/documents/{id}` | Soft-deletes (status `Deactivated`). `204`; `404` if absent. |

`ingestionStatus` is `Uploaded`, `Processing`, `Ingested`, `Failed`, or `Deactivated`.

Two caveats worth knowing before relying on this surface. **Deactivation does not remove the
document's chunks from the search index**, so a deactivated document stays retrievable. And a
document interrupted mid-ingest stays `Processing` permanently — there is no reset path, and a
re-ingest returns `409`.

---

## Retrieval debug

### `POST /api/debug/retrieval`

Administrator only. Returns raw retrieved chunks with their scores, for inspecting retrieval quality
directly.

Its own doc comment schedules it for removal now that the question-answering endpoints make it
redundant. It is not part of the product API surface; do not build against it.
