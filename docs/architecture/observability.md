# Observability

How the backend is instrumented so that any request — and any AI response — can be traced from the
HTTP call down to the evidence that produced it.

## Logging configuration

Logging is wired in one place: `ObservabilityExtensions` in
`backend/src/HarrisCountyAI.Api/Extensions/ObservabilityExtensions.cs`. `Program.cs` only calls
`builder.AddObservability()` and `app.UseObservability()`.

| Environment | Provider | Format |
|---|---|---|
| Development | Simple console | Single-line, `HH:mm:ss` timestamps, scopes included |
| All others | JSON console | Single-line JSON, UTC ISO-8601 timestamps, scopes included |

JSON console output makes every structured log property (`{DocumentId}`, `{StatusCode}`, …) a
parseable field for log aggregators. Always log with named message-template placeholders, never
string interpolation, so the properties survive as structured data.

## Correlation ids

`CorrelationIdMiddleware` runs first in the pipeline:

- An incoming `X-Correlation-Id` header is honored when it is 1–64 characters of
  `[A-Za-z0-9-_.:]`; anything else (missing, overlong, unsafe characters) is replaced with a
  generated 32-character id.
- The id is returned on every response in `X-Correlation-Id`, stored in
  `HttpContext.Items["CorrelationId"]`, and pushed onto the logging scope, so every log line
  written while handling the request carries a `CorrelationId` property.
- Callers (the Angular frontend, scripts, support engineers reproducing an issue) can supply their
  own id to stitch a browser action to the backend logs.

## Request logging

`RequestLoggingMiddleware` writes one structured event per request with `RequestMethod`,
`RequestPath`, `StatusCode`, and `ElapsedMilliseconds`. Unhandled exceptions are logged with the
exception and rethrown, so error handling behavior is unchanged.

## Domain pipeline events

Key pipeline stages log structured events through `ILogger<T>`:

| Stage | Source | Event |
|---|---|---|
| Document uploaded | `UploadDocumentHandler` | Document id, file name, size, type, case id |
| Extraction started | `DocumentProcessingService` | Document id, file name, case id |
| Extraction/normalization completed | `DocumentProcessingService` | Duration, page and field counts |
| Extraction failed | `DocumentProcessingService` | Exception, duration |
| Validation rule failure | `DocumentValidationService` | Rule name, case id, exception |

## AI request telemetry

`AiRequestTelemetry` (`backend/src/HarrisCountyAI.Application/Common/Telemetry/`) defines the
metadata captured for every AI question-answering request:

```text
Request ID, User ID, Case ID, Question, Model deployment, Prompt version,
Search filters, Retrieved chunk IDs, Retrieval scores, Reranking scores,
Latency, Token usage, Response status, Errors
```

`IAiRequestTelemetryLogger` is the contract question-answering calls once per request (success or
failure); `AiRequestTelemetryLogger` (`backend/src/HarrisCountyAI.Infrastructure/Telemetry/`) emits
it as a single structured log event.

Both question-answering paths record through it:

| Path | Records |
| --- | --- |
| `QuestionAnsweringService` | County-scoped and case-scoped questions |
| `DualSourceQuestionAnsweringService` | Comparisons, county evidence first then case |

Every exit path emits exactly one record — including the two that never reach the model (retrieval
found nothing, or the model call threw). Those are the requests most worth investigating, so
skipping them would hide the failures that matter most.

### How a record is stamped

The correlation id and the caller's identity are HTTP concerns, but only the Application layer knows
which AI call they belong to. `IRequestContextAccessor`
(`backend/src/HarrisCountyAI.Application/Common/Telemetry/`) carries them across that boundary;
`HttpRequestContextAccessor` (`backend/src/HarrisCountyAI.Api/Telemetry/`) implements it over
`IHttpContextAccessor`, reading the id assigned by `CorrelationIdMiddleware` and preferring the most
stable identity claim available (`oid`, then the subject, then the username).

Because the correlation id on the record is the same one returned in the `X-Correlation-Id` response
header, a reviewer who reports a bad answer and quotes that id can be traced to the exact model,
prompt version, and evidence that produced it.

Two deliberate behaviours:

- **Telemetry never fails an answer.** Recording is wrapped; a failing sink logs a warning and the
  answer is returned regardless. Losing a reviewer's answer to an observability outage would be a
  far worse failure than losing a record.
- **Calls with no HTTP request still emit.** The offline evaluation harness drives these services
  directly. Those records carry the placeholder ids in `AiTelemetryDefaults`, which are deliberately
  unmistakable so no reader confuses one for a real correlation id.

### Known gaps

- `SearchFilters` is left unset. The literal OData scope filter is built inside the retrieval
  implementation and is not surfaced by `IRetrievalService`, so it is not guessed at; `CaseId`
  already records the scope an auditor needs. Surfacing the real expression means widening the
  retrieval contract.
- `RerankingScores` aligns positionally with the chunk ids, so it is reported only when *every*
  retrieved chunk carries a reranker score. A partial set reports an empty list rather than padding
  with a fabricated `0.0`, which would read as "ranked last".

## What is never logged at `Information` and above

- Raw document content — neither uploaded case documents nor extracted text/normalized fields.
- Retrieved chunk **text** — telemetry carries chunk ids and scores only.
- Secrets or connection strings.

Identifiers (case ids, document ids, file names) and the user's question are acceptable log
content; document bodies are not.

### The `Debug` exception

`AzureLanguageModelService` logs the full system and user prompts when `Debug` logging is enabled
(`backend/src/HarrisCountyAI.Infrastructure/Azure/LanguageModels/AzureLanguageModelService.cs`). The
user prompt is the assembled evidence block, so at `Debug` level raw document and chunk text does
reach the logs. It is guarded by an `IsEnabled(LogLevel.Debug)` check and never appears at
`Information`.

**There is no redaction filter.** Nothing scrubs a log record before it is written — not for
document content, not for personal data on a permit application. Enabling `Debug` in an environment
holding real applicant documents would expose their contents, so the level is the only control, and
it is a blunt one. Adding redaction is genuine outstanding work, recorded here and in
[`security.md`](security.md) rather than left implicit.

## Application Insights

`Microsoft.ApplicationInsights.AspNetCore` telemetry is enabled only when
`ApplicationInsights:ConnectionString` is configured. The committed `appsettings.json` keeps it
empty, so local runs and tests emit console logs only; the real connection string is supplied via
environment configuration in Azure (App Service settings / Key Vault), never committed.

## Verifying locally

```bash
cd backend
dotnet test --filter "FullyQualifiedName~CorrelationId"
curl -i http://localhost:5096/health -H "X-Correlation-Id: my-trace-1"   # echoes the header back
```

(`http://localhost:5096` is the `http` launch profile; `https://localhost:7074` is the `https` one.)
