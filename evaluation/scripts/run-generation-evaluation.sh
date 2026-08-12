#!/usr/bin/env bash
# Runs the generation evaluation dataset through the question-answering
# pipeline and writes a report under evaluation/datasets/generation/results/.
#
#   ./run-generation-evaluation.sh              # offline, scripted answers, asserts the baseline
#   ./run-generation-evaluation.sh --update     # offline, rewrites the baseline
#   ./run-generation-evaluation.sh --live       # billable: one model completion per question
#
# See docs/evaluation/evaluation-strategy.md.

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_shared.sh"

parse_evaluation_args "$@"

if [[ "${MODE}" == "live" ]]; then
  run_evaluation_tests "FullyQualifiedName~LiveGenerationEvaluationTests"
  echo "Live report written to evaluation/datasets/generation/results/latest-live.json"
else
  run_evaluation_tests "FullyQualifiedName~GenerationEvaluation"
  echo "Fixture baseline at evaluation/datasets/generation/results/baseline-fixture.json"
fi
