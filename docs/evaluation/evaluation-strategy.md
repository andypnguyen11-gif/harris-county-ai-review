# Evaluation Strategy

How this project measures whether its RAG answers are any good, and why the
measurement is split the way it is.

## Why retrieval and generation are measured separately

A wrong answer from a RAG system has two very different causes, and one number
cannot tell them apart:

```text
Question
   ↓
Retrieval  ──── wrong evidence retrieved  →  the model never had a chance
   ↓
Generation ──── right evidence, wrong answer  →  the model is the problem
   ↓
Answer
```

So the harness measures them separately. Retrieval evaluation asks *did the
system put the correct evidence in front of the model?* — a question that can be
answered with no model in the loop at all, using deterministic matching rules.
Generation evaluation asks the second question, and only makes sense once the
first is under control.

This document covers retrieval evaluation. Generation and judge evaluation are
layered on top of the same harness.

## What lives where

```text
evaluation/
  datasets/retrieval/floodplain-questions.json   the curated question set
  datasets/retrieval/results/baseline-fixture.json   committed offline baseline
  datasets/retrieval/results/latest-live.json    written by a live run (not committed by default)
  fixtures/retrieval/fixture-corpus.json         synthetic corpus for offline runs
  scripts/run-retrieval-evaluation.sh            the runner entry point

backend/src/HarrisCountyAI.Application/Evaluation/    dataset, matcher, metrics, runner
backend/tests/HarrisCountyAI.IntegrationTests/Evaluation/   harness wiring and gates
```

The scoring logic lives in the Application layer, next to the retrieval
abstraction it exercises, so it is unit tested like any other code and can run
against either the real Azure retrieval service or an offline stand-in without
changing a line.

## The dataset

`evaluation/datasets/retrieval/floodplain-questions.json` holds 28 hand-written
floodplain-permit questions. Each records the corpus source(s) a good retrieval
should surface:

```json
{
  "id": "section-01",
  "category": "section-number",
  "question": "What does Section 4.2 of the Harris County floodplain regulations require?",
  "expectedSources": [
    { "title": "Regulations of Harris County for Floodplain Management", "section": "Section 4.2", "page": null }
  ]
}
```

Questions carry a **category**, because retrieval modes trade off differently
across them and an aggregate number hides that:

| Category | What it tests | Why it matters |
|---|---|---|
| `section-number` | "What does Section 4.2 require?" | Exact-token lookups, where keyword search beats pure vector search |
| `form-number` | "When do I use Form MT-EZ?" | Form and acronym recall, another lexical strength |
| `semantic` | "Can I build a shed in the floodplain?" | Plain-language paraphrase, where vector search earns its place |
| `mixed` | A section number *and* a paraphrase in one question | The case hybrid retrieval exists for |

Semantic questions outnumber every other category on purpose — that is how
reviewers and applicants actually ask.

**Expected sources are alternatives, not a checklist.** A question is a hit when
*any one* of its listed sources appears in the results, so a question whose
answer legitimately lives in either the regulations or a form instruction is not
scored as a failure for finding one of them.

### Matching rules

Matching is deterministic — no model, no embeddings — so a recall number means
the same thing in every run:

- **Title** must match after normalization (lowercased, punctuation dropped,
  whitespace collapsed). `Floodplain Regulations (2024)` matches
  `Floodplain Regulations 2024`.
- **Section** is checked only when the expectation records one. A section is
  satisfied by itself or by a subsection: `Section 4.2` is satisfied by
  `Section 4.2 Permit Application Requirements` and by `Section 4.2.1`, but not
  by `Section 42`. This keeps the dataset stable as chunking granularity changes.
- **Page** is checked only when the expectation records one, with a one-page
  tolerance because chunks straddle page boundaries.

Page expectations are currently `null` throughout. The scorer supports them and
they are tested, but recording a page number that was not read off the ingested
PDF would silently turn a question into a guaranteed miss. They get filled in
when the corpus is ingested and the numbers can be verified.

## Metrics

| Metric | Definition |
|---|---|
| `Recall@1` | Share of questions with an expected source at rank 1 |
| `Recall@3` | Share of questions with an expected source in the top 3 |
| `Recall@5` | Share of questions with an expected source in the top 5 |
| `MRR` | Mean of `1 / rank of the first expected source`; a miss contributes 0 |

Each question has one correct piece of evidence, so Recall@K here is the
hit-rate the PRD asks for. MRR adds the ordering information Recall@K discards:
two configurations can both find the evidence within the top 5 while one
consistently ranks it first — which is what the model actually sees.

All four are reported overall and per category. A retrieval change that improves
the aggregate while regressing `section-number` is a regression.

## Running it

```bash
# Offline, free, reproducible. Asserts the committed baseline.
evaluation/scripts/run-retrieval-evaluation.sh

# Same, but rewrite the baseline (review the diff before committing).
evaluation/scripts/run-retrieval-evaluation.sh --update

# Billable: one embedding call and one search query per question.
evaluation/scripts/run-retrieval-evaluation.sh --live
```

### Fixture runs and live runs

Every result file records a `runType`, and the distinction is the most important
thing about it.

**`Fixture`** runs retrieve from `evaluation/fixtures/retrieval/fixture-corpus.json`
— a small synthetic corpus written for this harness — using a BM25-style lexical
ranker. Its passages are *not* the text of the Harris County regulations or of
any FEMA form and must never be quoted as authority. A fixture run costs
nothing, needs no Azure account, and is byte-reproducible, so the committed
`baseline-fixture.json` acts as a regression gate: change the dataset, the
matcher, the metrics, or the runner and the diff shows up in CI.

A fixture run does **not** measure production retrieval quality. Plain lexical
ranking over 28 passages is not hybrid search with embeddings and semantic
reranking, and the absolute numbers should never be quoted as if it were.

**`Live`** runs retrieve from the configured Azure AI Search index. They are the
only runs whose numbers describe the real system, and they cost money — which is
why they are gated behind `RUN_EVALUATION=1` plus the Azure settings, why every
live test skips by default, and why `dotnet test` on a developer machine or in
CI runs the fixture harness only.

Credentials for a live run are read from `~/.harriscountyai/azure.env` (override
with `AZURE_ENV_FILE`), which lives outside the repository. Committed
configuration carries empty-string placeholders only.

## Comparing two retrieval configurations

The comparison `docs/architecture/rag-architecture.md` describes — vector-only
vs hybrid, and reranking on vs off — runs like this:

1. Ingest the reference corpus into the index.
2. Run `--live` once per configuration, changing `Retrieval__Mode` or
   `Reranking__Enabled` between runs, and keep each `latest-live.json`.
3. Compare `Recall@K` and `MRR` **per category**.

Hybrid should at minimum match vector-only on `semantic` questions and beat it
on `section-number` and `form-number`. A regression in any category blocks the
change.

## Known limitations

- **28 questions is small.** The dataset catches relative regressions between
  configurations; it does not produce a confidence interval, and a single
  question flipping moves `Recall@1` by 3.6 points.
- **Expectations are hand-written, not adjudicated.** Section numbers were
  written against the corpus structure, not verified against ingested page
  images. Until the corpus is ingested, a "miss" may mean the expectation is
  wrong rather than the retrieval.
- **The fixture baseline is a harness regression gate, not a quality
  benchmark.** Recall@5 sits at 1.0 on the fixture corpus, which means that
  particular cutoff has no headroom to detect a fixture-side regression;
  `Recall@1` and `MRR` carry the signal there.
- **No live baseline is committed.** The committed numbers are all
  `runType: Fixture`. Producing a live baseline requires an ingested corpus and
  spends Azure credits.
- **Nothing fails CI on a live regression.** The fixture baseline is asserted in
  CI; live results are not, because CI has no Azure credentials by design.
