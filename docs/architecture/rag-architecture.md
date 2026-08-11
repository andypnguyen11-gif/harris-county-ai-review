# RAG Architecture: The Search Index

This document describes the Azure AI Search index that backs retrieval — its schema, its vector configuration, and the guarantee that keeps case evidence and the Harris County reference corpus separate. See [`system-overview.md`](system-overview.md) for the wider architecture.

## One Index, Two Corpora

The system retrieves from two knowledge domains:

- the **Harris County reference corpus** — curated regulations, checklists, forms, and guidance ingested by administrators, and
- **case-specific uploaded documents** — the evidence an applicant submitted for one case.

They share a single physical index (`harris-county-chunks`) rather than two. Reasons:

- **One schema, one pipeline.** Both corpora are chunked, embedded, and indexed identically, so a second index would duplicate the schema, the ingestion path, and the vector configuration with no behavioral difference.
- **Free-tier limits.** The dev search service (free tier) allows only three indexes; one shared index leaves room for experiments and future needs.
- **Separation is a query concern, not a storage concern.** Retrieval must sometimes scope to the corpus, sometimes to one case's documents — but never blend the two into one undifferentiated pool. Filters express that precisely.

### The separation guarantee

Every chunk carries two discriminator fields:

| Field | Values | Purpose |
|---|---|---|
| `sourceType` | `KnowledgeBase` \| `CaseDocument` | Which corpus the chunk belongs to |
| `caseId` | GUID or null | The owning case; always null for `KnowledgeBase` chunks |

**Every query issued against this index must apply a `sourceType` filter, and case-document queries must additionally filter `caseId` to the case under review.** Retrieval services (PR-23 onward) own this rule; no query path may search the index unfiltered. This is how the project principle — case evidence and the county corpus are indexed, retrieved, and cited separately — is enforced at query time:

```text
County requirements question:   $filter=sourceType eq 'KnowledgeBase'
Case evidence question:         $filter=sourceType eq 'CaseDocument' and caseId eq '<case guid>'
```

Because the discriminators are plain filterable fields written at indexing time (and validated then — the index service rejects unknown source types), a chunk can never appear in the wrong scope: a knowledge-base chunk has no `caseId` to match, and a case chunk can never satisfy the `KnowledgeBase` filter.

## Index Schema

Defined in code by `SearchIndexDefinition` (`backend/src/HarrisCountyAI.Infrastructure/Azure/Search/SearchIndexDefinition.cs`), deployed idempotently by `IDocumentIndexService.EnsureIndexAsync()`.

| Field | Type | Attributes | Purpose |
|---|---|---|---|
| `chunkId` | String | **key**, filterable | Unique chunk key: `{documentId:N}-{sequence:D4}` |
| `documentId` | String | filterable | Parent document; used to delete/re-index a whole document |
| `sourceType` | String | filterable, facetable | Corpus discriminator (see above) |
| `title` | String | searchable, filterable | Source document title; searchable so titles boost keyword recall |
| `department` | String | filterable, facetable | Owning county department |
| `permitType` | String | filterable, facetable | Permit type (e.g. Floodplain) |
| `documentType` | String | filterable, facetable | Regulation, form, checklist, … |
| `section` | String | filterable | Section heading the chunk came from; cited in answers |
| `page` | Int32 | filterable | Page number; cited in answers |
| `effectiveDate` | DateTimeOffset | filterable, sortable | When the source document took effect |
| `sourceUrl` | String | retrievable only | Public URL for citation links |
| `text` | String | searchable | The chunk content (standard Lucene analyzer) |
| `embedding` | Collection(Single) | vector, 1536 dims | Vector for semantic similarity search |
| `caseId` | String | filterable | Owning case for `CaseDocument` chunks; null otherwise |

Field-attribute choices:

- **Filterable metadata, searchable text.** Metadata fields exist to scope and facet queries deterministically, so they are filterable (and facetable where an admin UI is likely to aggregate on them). Full-text relevance comes only from `text` and `title`, both analyzed with the standard Lucene analyzer — the county corpus is plain English prose and needs no custom analyzer.
- **`chunkId` embeds the sequence** (`-0000`, `-0001`, …), so chunk order within a document is recoverable from the key without a dedicated field.
- **GUIDs are stored as lowercase `D`-format strings** (`documentId`, `caseId`); the index service formats filters the same way, so equality filters always match.

## Vector Configuration

- **Profile:** `chunk-vector-profile`, applied to the `embedding` field.
- **Algorithm:** HNSW (`chunk-hnsw`) with **cosine** similarity and SDK-default graph parameters (m=4, efConstruction=400, efSearch=500).
- **Dimensions:** 1536, matching `text-embedding-3-small` — the embedding model configured in PR-20.

Why HNSW over exhaustive KNN: HNSW is the standard approximate-nearest-neighbor choice on Azure AI Search — sub-linear query time as the corpus grows, at a negligible recall cost for a corpus of this size. Cosine is the metric OpenAI-family embeddings are trained for. Defaults are kept because the corpus (hundreds of documents, thousands of chunks) is far below the scale where HNSW graph tuning pays off; parameters live in one place (`SearchIndexDefinition`) if that changes.

Filters combine with vector queries natively in Azure AI Search (pre-filtered vector search), so the separation guarantee holds for keyword, vector, and hybrid retrieval alike.

## Indexing Operations

`IDocumentIndexService` (Application layer) exposes three operations, implemented by `AzureDocumentIndexService` (Infrastructure):

- **`EnsureIndexAsync`** — creates or updates the index from `SearchIndexDefinition`. Idempotent; safe to run at deployment or startup.
- **`IndexAsync(chunks)`** — upserts chunks by `chunkId`. Callers supply `IndexableChunk` values that carry the precomputed 1536-dimension embedding; the index service validates dimensions and `sourceType` before upload and has no dependency on the embedding service.
- **`DeleteDocumentAsync(documentId)`** — finds every chunk whose `documentId` matches and deletes them by key. Re-indexing a document is delete-then-index, which also removes stale chunks when a document shrinks.

The Azure SDK is wrapped behind a thin `ISearchIndexGateway` seam so the mapping and delete logic are unit-testable without a live service; an integration test (skipped unless `SEARCH_ENDPOINT` is configured) deploys the schema against the real service and round-trips a sample chunk.
