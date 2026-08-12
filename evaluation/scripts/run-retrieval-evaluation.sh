#!/usr/bin/env bash
# Scores the retrieval evaluation dataset and writes a report under
# evaluation/datasets/retrieval/results/.
#
#   ./run-retrieval-evaluation.sh              # offline fixture run, asserts the baseline
#   ./run-retrieval-evaluation.sh --update     # offline fixture run, rewrites the baseline
#   ./run-retrieval-evaluation.sh --live       # billable run against Azure AI Search
#
# See docs/evaluation/evaluation-strategy.md.

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_shared.sh"

parse_evaluation_args "$@"

if [[ "${MODE}" == "live" ]]; then
  run_evaluation_tests "FullyQualifiedName~LiveRetrievalEvaluationTests"
  echo "Live report written to evaluation/datasets/retrieval/results/latest-live.json"
else
  run_evaluation_tests "FullyQualifiedName~RetrievalEvaluation"
  echo "Fixture baseline at evaluation/datasets/retrieval/results/baseline-fixture.json"
fi
