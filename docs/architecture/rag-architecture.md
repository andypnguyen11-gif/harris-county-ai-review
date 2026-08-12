# RAG Architecture: Indexing and Retrieval

This document describes the Azure AI Search index that backs retrieval — its schema, its vector configuration, the guarantee that keeps case evidence and the Harris County reference corpus separate — and the retrieval strategy that queries it. See [`system-overview.md`](system-overview.md) for the wider architecture.

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

## Retrieval Strategy

Corpus retrieval (`AzureRetrievalService`, behind the `IRetrievalService` seam) issues **hybrid queries** by default: the raw question goes to the service as keyword search text *and* as a 1536-dimension embedding for vector search, in a single request. Azure AI Search runs both legs and fuses their rankings with Reciprocal Rank Fusion (RRF), so a chunk that ranks well on either leg surfaces in the merged result.

Why hybrid instead of vector-only:

- **Keyword search wins on exact identifiers.** Section numbers ("Section 4.2"), form numbers ("MT-EZ", "Elevation Certificate"), and regulatory references are near-opaque to embeddings — semantically, "Section 4.2" and "Section 5.1" are almost the same vector — but are trivially matched by the inverted index over `text` and `title`.
- **Vector search wins on paraphrase.** "Can I build a shed behind my house?" shares almost no vocabulary with "accessory structures in the regulatory floodplain", but their embeddings are close.
- County permit questions arrive in both shapes, often mixed in one question.

Configuration (the `Retrieval` section, every setting defaulted):

| Setting | Default | Purpose |
|---|---|---|
| `Retrieval:Mode` | `Hybrid` | `Hybrid` or `VectorOnly`; `VectorOnly` exists for A/B comparison |
| `Retrieval:DefaultTopK` | 5 | Result count when a `RetrievalRequest` does not specify `TopK` |

Callers can still set `TopK` per request (1–50); the corpus `sourceType` filter applies identically in both modes because Azure AI Search pre-filters the keyword and vector legs alike.

Every retrieval logs its metrics — mode, requested and returned counts, top/bottom relevance scores, embedding and search durations — so retrieval quality and latency can be watched without logging question text.

### Semantic reranking

When enabled, retrieval adds a reranking stage on top of hybrid search:

```text
Hybrid search  →  candidate pool (Reranking:CandidatePoolSize, default 20)
      ↓
Azure semantic ranking (IRerankingService / AzureSemanticRerankingService)
      ↓
Best TopK chunks (callers keep asking for 3–5) into the answer context
```

The reranker re-issues the question as a semantic keyword query scoped to exactly the candidate chunks (a `search.in` filter over their ids, always combined with the corpus `sourceType` filter), and reorders the candidates by the reranker scores Azure reports. Each reranked chunk carries its score in `RetrievedChunk.RerankerScore` (0–4; null when not reranked); the original hybrid relevance score is kept alongside.

Configuration (the `Reranking` section, every setting defaulted):

| Setting | Default | Purpose |
|---|---|---|
| `Reranking:Enabled` | `false` | Turns reranking on; also adds the semantic configuration (`chunk-semantic`, ranking over `title` + `text`) to the index on the next `EnsureIndexAsync` |
| `Reranking:SemanticConfigurationName` | `chunk-semantic` | Semantic configuration to rank with |
| `Reranking:CandidatePoolSize` | 20 | Hybrid candidates retrieved for the reranker (1–50; Azure rescoring caps at 50) |

Reranking **fails open**: semantic ranker is a service-tier capability the free tier lacks, so it is off by default, the index only carries the semantic configuration when it is on (adding it later is an in-place index update — no re-creation or re-indexing), and any semantic-query failure at runtime logs a warning and falls back to plain hybrid order. The application never depends on the reranker being available.

The before/after comparison uses the same dataset and methodology as the vector-vs-hybrid comparison below, toggling `Reranking:Enabled` instead of `Retrieval:Mode`.

### Comparing vector-only and hybrid retrieval

The evaluation dataset at [`evaluation/datasets/retrieval/floodplain-questions.json`](../../evaluation/datasets/retrieval/floodplain-questions.json) holds floodplain-permit questions in three categories — `section-number`, `form-number`, and `semantic` — each with the corpus source(s) a good retrieval should surface. The comparison methodology:

1. Ingest the reference corpus into the index (knowledge-base upload flow).
2. For each dataset question, run retrieval once with `Retrieval:Mode = VectorOnly` and once with `Hybrid` (the debug endpoint `POST /api/debug/retrieval` returns raw chunks with scores).
3. Score each run per category: **hit rate** (an expected source appears in the top K, matching on title and — when given — section) and **MRR** (reciprocal rank of the first expected source).
4. Hybrid should at minimum match vector-only on `semantic` questions and beat it on `section-number` / `form-number` questions; a regression in any category blocks the retrieval change.

The dataset is deliberately small and curated; it exists to catch relative regressions between retrieval modes, not to benchmark absolute quality.

This comparison now runs automatically: `evaluation/scripts/run-retrieval-evaluation.sh --live` scores the dataset against the configured index and writes Recall@1/3/5 and MRR, overall and per category, to `evaluation/datasets/retrieval/results/`. Run it once per configuration and diff the reports. The same harness runs offline against a synthetic fixture corpus by default, so a plain `dotnet test` regression-tests the scorer without touching Azure. See [`docs/evaluation/evaluation-strategy.md`](../evaluation/evaluation-strategy.md).
