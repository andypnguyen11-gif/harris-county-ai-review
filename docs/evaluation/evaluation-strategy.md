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

So the harness measures them separately. **Retrieval evaluation** asks *did the
system put the correct evidence in front of the model?* — answerable with no
model in the loop at all, using deterministic matching rules. **Generation
evaluation** asks the second question, and only makes sense once the first is
under control.

## What lives where

```text
evaluation/
  datasets/retrieval/floodplain-questions.json        curated retrieval questions
  datasets/retrieval/results/baseline-fixture.json    committed offline baseline
  datasets/generation/questions.json                  expected outcomes, facts, citations
  datasets/generation/manual-review.json              human labels the judge is measured against
  datasets/generation/results/baseline-fixture.json   committed offline generation baseline
  datasets/generation/results/judge-baseline-fixture.json   committed offline judge baseline
  datasets/*/results/*latest-live.json                written by a live run (not committed)
  fixtures/retrieval/fixture-corpus.json              synthetic corpus for offline runs
  fixtures/generation/scripted-answers.json           hand-written answers for offline runs
  fixtures/judging/scripted-verdicts.json             hand-written verdicts for offline runs
  scripts/run-retrieval-evaluation.sh                 retrieval runner
  scripts/run-generation-evaluation.sh                generation runner
  scripts/run-judge-evaluation.sh                     judge runner

backend/src/HarrisCountyAI.Application/Evaluation/          datasets, scorers, metrics, runners
backend/tests/HarrisCountyAI.IntegrationTests/Evaluation/   harness wiring and cost gates
```

The scoring logic lives in the Application layer, next to the abstractions it
exercises, so it is unit tested like any other code and runs unchanged against
real Azure services or offline stand-ins.

## Fixture runs and live runs

Every result file records a `runType`, and the distinction is the most important
thing about it.

**`Fixture`** runs use committed synthetic stand-ins: a small corpus in
`evaluation/fixtures/retrieval/` retrieved with a BM25-style lexical ranker,
and, for generation, hand-written answers in `evaluation/fixtures/generation/`
replayed through the real pipeline. Neither is the text of the Harris County
regulations or of any FEMA form, and neither may be quoted as authority. A
fixture run costs nothing, needs no Azure account, and is byte-reproducible, so
the committed `baseline-fixture.json` files act as regression gates: change a
dataset, a scorer, a prompt, or the citation resolver and the diff shows up in
CI.

A fixture run does **not** measure production quality. Lexical ranking is not
hybrid search with embeddings and reranking, and a scripted answer is not a
model. The absolute numbers should never be quoted as if they were.

**`Live`** runs call the configured Azure services. They are the only runs whose
numbers describe the real system, and they cost money — which is why every live
test is gated behind `RUN_EVALUATION=1` plus the required Azure settings, skips
by default, and never runs in CI.

Credentials come from `~/.harriscountyai/azure.env` (override with
`AZURE_ENV_FILE`), which lives outside the repository. Committed configuration
carries empty-string placeholders only.

---

## Retrieval evaluation

### The dataset

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
*any one* of its listed sources appears, so a question whose answer legitimately
lives in either the regulations or a form instruction is not scored as a failure
for finding one of them.

#### Matching rules

Matching is deterministic — no model, no embeddings — so a recall number means
the same thing in every run:

- **Title** must match after normalization (lowercased, punctuation dropped,
  whitespace collapsed). `Floodplain Regulations (2024)` matches
  `Floodplain Regulations 2024`.
- **Section** is checked only when recorded, and is satisfied by itself or by a
  subsection: `Section 4.2` is satisfied by `Section 4.2 Permit Application
  Requirements` and by `Section 4.2.1`, but not by `Section 42`. This keeps the
  dataset stable as chunking granularity changes.
- **Page** is checked only when recorded, with a one-page tolerance because
  chunks straddle page boundaries.

Page expectations are currently `null` throughout. The scorer supports them and
they are tested, but recording a page number that was not read off the ingested
PDF would silently turn a question into a guaranteed miss. They get filled in
when the corpus is ingested.

### Metrics

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

### Running it

```bash
evaluation/scripts/run-retrieval-evaluation.sh            # offline, free
evaluation/scripts/run-retrieval-evaluation.sh --update   # rewrite the baseline
evaluation/scripts/run-retrieval-evaluation.sh --live     # billable
```

### Comparing two retrieval configurations

The comparison `docs/architecture/rag-architecture.md` describes — vector-only
vs hybrid, reranking on vs off — runs like this:

1. Ingest the reference corpus into the index.
2. Run `--live` once per configuration, changing `Retrieval__Mode` or
   `Reranking__Enabled` between runs, keeping each `latest-live.json`.
3. Compare `Recall@K` and `MRR` **per category**.

Hybrid should at minimum match vector-only on `semantic` questions and beat it
on `section-number` and `form-number`. A regression in any category blocks the
change.

---

## Generation evaluation

Given the evidence, did the model answer correctly — and did it decline when it
should have?

### The dataset

`evaluation/datasets/generation/questions.json` holds 18 questions in two
categories:

- **`answerable` (15)** — the corpus supports an answer. Each records the facts
  a correct answer must state and the documents it should cite.
- **`out-of-scope` (3)** — the corpus does not support an answer (a tax rate, a
  permit's status, a request for legal advice). The expected outcome is
  `InsufficientEvidence`. A fluent answer here is a hallucination however well
  it reads, and this category is the only thing that measures it.

```json
{
  "id": "gen-elevation",
  "category": "answerable",
  "question": "How high must the lowest floor of a new house be built in the floodplain?",
  "expectedOutcome": "Answered",
  "expectedFacts": [
    {
      "id": "freeboard",
      "description": "States the required elevation above the base flood elevation",
      "requiredPhrases": ["one foot"],
      "anyOfPhrases": ["base flood elevation", "bfe"]
    }
  ],
  "expectedCitationTitles": ["Regulations of Harris County for Floodplain Management"]
}
```

**Facts are phrase requirements, not a reference answer.** A regulatory
requirement has many correct wordings, and scoring against one would punish a
correct paraphrase. A fact is covered when every `requiredPhrases` entry appears
and — when `anyOfPhrases` is given — at least one alternative does. Matching is
case-, punctuation-, and whitespace-insensitive, on word boundaries.

### What is measured

The runner drives the **real** question-answering pipeline: retrieval, the
grounded prompt, the JSON response contract, citation resolution, and the
fail-closed downgrade that turns an uncitable answer into an
insufficient-evidence response. What a reviewer would actually see is what gets
scored.

| Metric | Definition |
|---|---|
| `OutcomeMatchRate` | Share of questions that concluded the way the dataset expected |
| `CitationPresenceRate` | Share of answered responses carrying at least one citation |
| `CitationTitleAccuracy` | Share of answered responses whose citations all named an expected document |
| `MeanFactCoverage` | Mean share of expected facts stated |
| `FullFactCoverageRate` | Share of questions whose answer stated *every* expected fact |
| `UnsupportedClaimRate` | Share of answer sentences below the lexical support threshold |
| `AnswersWithUnsupportedClaimsRate` | Share of answers containing at least one such sentence |

A rate with no applicable questions is `null`, not `0` — "no data" and "zero
percent" are very different findings.

`CitationPresenceRate` is a **contract, not a score**. The pipeline downgrades an
answer it cannot cite, so anything below 1.0 is a defect in the pipeline rather
than a weak run.

### Unsupported-claim detection

Each answer sentence is scored on the share of its content words that appear
anywhere in the passages the model was actually given. Below the threshold
(default 0.6) the sentence is flagged.

The evidence comes from a `RecordingRetrievalService` that decorates the
retrieval the pipeline itself used. Re-running retrieval afterwards would not
do: a second query can return different chunks, and the evaluation would then be
scoring the answer against evidence the model never saw.

**This is a cheap screen, not a groundedness verdict, and it is wrong in both
directions.** The committed fixture baseline demonstrates each failure mode
rather than hiding it:

- *True positive* — `gen-penalties` asserts a dollar figure the corpus never
  states. Flagged, with the fabricated tokens listed.
- *False positives* — `gen-substantial-improvement` and `gen-manufactured-home`
  summarize the evidence faithfully but in vocabulary the passages never used
  ("held to the same standards"). A token-overlap screen cannot tell those from
  an invention.
- *False negative* — a claim assembled entirely from evidence vocabulary passes.
  "The lowest floor may be one foot **below** the base flood elevation" scores a
  perfect 1.0. This case is pinned in the unit tests.

That precision ceiling is the price of a check that is free, deterministic, and
always on. The semantic version — reasoning about entailment rather than
counting tokens — is the LLM judge, which is a separate, opt-in step.

### Running it

```bash
evaluation/scripts/run-generation-evaluation.sh            # offline, free
evaluation/scripts/run-generation-evaluation.sh --update   # rewrite the baseline
evaluation/scripts/run-generation-evaluation.sh --live     # billable: one completion per question
```

The offline run replays `evaluation/fixtures/generation/scripted-answers.json`
through the real pipeline. The scripted model is bound to the dataset by
question id — an unmatched entry on either side is an error, not a silent gap —
and it cites by **document title**, resolving titles against the numbered
sources actually present in the prompt. A scripted answer therefore cannot cite
evidence retrieval never surfaced: when the expected document is missing, the
entry degrades to insufficient evidence, exactly as a well-behaved model should.

---

## LLM-as-a-judge evaluation

The deterministic checks above are cheap and reproducible, but they are literal.
They cannot tell a correct paraphrase from a subtly wrong one, and they cannot
tell a faithful summary from an invention when both use the same vocabulary. The
judge is the semantic layer that can — at the cost of a model call per answer.

It is a **development-time evaluator, not a production dependency.** The PRD is
explicit that a judge should prove itself as an evaluation capability before it
ever sits in a request path, and `AddAnswerJudge` is deliberately not called from
the application's composition root: putting it there would add a second model
call, and a second thing to be wrong, to every answer a reviewer sees.

### Criteria

All five score 1–5 on the same scale, in the same direction — higher is always
better — so an aggregate means something and nobody has to remember which way
one of them points.

| Criterion | Question it answers |
|---|---|
| `Groundedness` | Is every claim traceable to the supplied evidence? |
| `Relevance` | Does the answer address the question that was asked? |
| `Completeness` | Does it cover what the evidence supports? |
| `Accuracy` | Does it state the evidence correctly, without reversing or overstating it? |
| `UnsupportedClaims` | Freedom from claims beyond the evidence — 5 means none |

A case counts as **acceptable** only when *every* criterion clears the threshold
(4 by default). One weak criterion sinks it: a perfectly relevant, complete
answer that invents a fact is not acceptable, and averaging would hide that.

The judge is told to score against the supplied evidence only, and never to
reward an answer for stating something true that the evidence does not contain.
It is also told that correctly reporting insufficient evidence is good
behaviour — a judge that punished a correct refusal would push the product
toward answering everything, which is the opposite of what it is for.

### Prompt-injection hygiene

The judge sees three untrusted inputs at once: the question, the retrieved
corpus text, and the generated answer. Each is delimited, delimiter tokens
inside the data are neutralized so a block cannot close itself and address the
judge directly, and the system prompt names all three as data. It additionally
tells the judge that an answer asking to be scored highly is, by that fact, less
trustworthy.

### Failing closed

A model error, unparseable JSON, a missing criterion, or a score outside 1–5 all
yield `UnableToJudge` — never a partial verdict, never a substituted default.
This matters more for a judge than for most services: one that quietly invented a
3 when it could not read the response would pull every aggregate toward the
middle and hide exactly the regressions it was built to find. An unjudged case is
excluded from the metrics rather than counted as a bad answer, because the judge
failing says nothing about the answer.

### Checking the judge against a human

An automated judge is worth exactly as much as its agreement with a person on
cases a person has looked at. `evaluation/datasets/generation/manual-review.json`
carries a hand-written verdict and reason for every generated answer, and every
judge run reports `ManualAgreementRate` next to its own scores. A prompt change
that makes the judge more agreeable rather than more accurate shows up as
falling agreement, not rising scores.

The committed fixture baseline contains one deliberate disagreement
(`gen-substantial-improvement`, where the scripted judge is harsher than the
human about a generalizing sentence), so the agreement metric is provably not
stuck at 1.0. A test asserts that.

### Running it

```bash
evaluation/scripts/run-judge-evaluation.sh            # offline, free
evaluation/scripts/run-judge-evaluation.sh --update   # rewrite the baseline
evaluation/scripts/run-judge-evaluation.sh --live     # the most expensive run in the harness
```

A live judge run costs **two model completions per question** — one to answer,
one to judge — plus an embedding call and a search query each. That is why the
runner takes answer transcripts rather than re-answering the dataset: one
generation pass feeds both the generation report and the judge report, and the
judge grades the answers that were actually measured.

---

## Known limitations

### Retrieval

- **28 questions is small.** The dataset catches relative regressions between
  configurations; it does not produce a confidence interval, and a single
  question flipping moves `Recall@1` by 3.6 points.
- **Expectations are hand-written, not adjudicated.** Section numbers were
  written against the corpus structure, not verified against ingested page
  images. Until the corpus is ingested, a "miss" may mean the expectation is
  wrong rather than the retrieval.
- **Recall@5 sits at 1.0 on the fixture corpus**, so that cutoff has no headroom
  to detect a fixture-side regression; `Recall@1` and `MRR` carry the signal
  there. More distractor passages would restore it.
- **Expected pages are all `null`.** The mechanism is built and tested; the
  values are not recorded yet.

### Generation

- **Expected facts are provisional.** They were written against the corpus
  structure and the fixture corpus, and must be re-verified against the ingested
  PDFs before a live run's fact coverage is read as a quality measurement.
- **Fact coverage is literal.** It catches an answer that omitted the number or
  the condition; it cannot catch an answer that used the right words to say the
  wrong thing.
- **Unsupported-claim detection has a real precision ceiling**, documented and
  pinned above. Treat the rate as a tripwire, not a groundedness score.
- **Only county-scope questions are covered.** Case-scoped and dual-source
  answering would need seeded case documents, which this dataset does not carry.
- **Three out-of-scope questions is thin** for measuring refusal behaviour,
  which is arguably the most important property the product has.

### The judge

- **The committed judge baseline is a scripted fixture, not a model.** It proves
  the prompt is built, the response contract is parsed, the threshold is applied,
  and agreement is computed. It says nothing about how a real model would score
  these answers.
- **The judge has never been run live**, so its agreement with the human labels
  is entirely unmeasured. Until it has been, no judge score should carry weight
  in a decision.
- **Eighteen human labels, one reviewer, no adjudication.** There is no
  inter-rater agreement figure, and the same person wrote the fixture answers and
  labeled them, which is a real source of bias.
- **Only one label is `Unacceptable`.** Agreement is therefore dominated by the
  easy cases; a judge that marked everything acceptable would score 94% on this
  set. The rate needs adversarial examples before it means much.
- **Acceptability is a single threshold over five criteria.** It is a blunt
  summary; the per-criterion scores are what actually diagnose a problem.
- **A judge is a model**, with the same failure modes as the model it grades —
  including agreeing with a confident, well-written, wrong answer.

### Both

- **No live baseline is committed.** Every committed number is
  `runType: Fixture`. Producing a live baseline requires an ingested corpus and
  spends Azure credits.
- **Nothing fails CI on a live regression.** CI asserts the fixture baselines
  only, because CI has no Azure credentials by design. A live regression is
  caught by a human running the script and diffing the report.
