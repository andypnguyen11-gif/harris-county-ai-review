#!/usr/bin/env bash
# Grades generated answers with the LLM judge and writes a report under
# evaluation/datasets/generation/results/.
#
#   ./run-judge-evaluation.sh              # offline, scripted judge, asserts the baseline
#   ./run-judge-evaluation.sh --update     # offline, rewrites the baseline
#   ./run-judge-evaluation.sh --live       # billable: TWO model completions per question
#
# The live mode is the most expensive run in the harness — one completion to
# answer each question and one to judge the answer. See
# docs/evaluation/evaluation-strategy.md.

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_shared.sh"

parse_evaluation_args "$@"

if [[ "${MODE}" == "live" ]]; then
  run_evaluation_tests "FullyQualifiedName~LiveJudgeEvaluationTests"
  echo "Live judge report written to evaluation/datasets/generation/results/judge-latest-live.json"
else
  run_evaluation_tests "FullyQualifiedName~JudgeEvaluation"
  echo "Fixture baseline at evaluation/datasets/generation/results/judge-baseline-fixture.json"
fi
