#!/usr/bin/env bash
#
# Compiles every Bicep template and asserts the tuning that the deployed
# environment depends on.
#
# The assertions exist because these particular defaults were once wrong in a
# way no compiler could catch: the templates were authored against the free
# tiers, the live resources were later tuned by hand to make corpus ingestion
# work, and the two drifted apart. Redeploying would have quietly reverted the
# live resources to values that still deploy cleanly and still return HTTP 200,
# while truncating documents and throttling ingestion. A green deployment is
# not evidence of a correct one, so the requirements are asserted here.
#
# Usage:
#   infra/validate.sh
#
# Requires the Azure CLI (which bundles Bicep). Run from anywhere.

set -euo pipefail

infra_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
failures=0

fail() {
  echo "  FAIL: $*" >&2
  failures=$((failures + 1))
}

pass() {
  echo "  ok: $*"
}

echo "Compiling Bicep templates"

# Bicep reports lint warnings on stderr while still exiting 0, so a clean exit
# code is not enough; anything written to stderr is treated as a failure.
for template in "$infra_dir"/main.bicep "$infra_dir"/modules/*.bicep; do
  name="${template#"$infra_dir"/}"
  if ! stderr="$(az bicep build --file "$template" --stdout 2>&1 >/dev/null)"; then
    fail "$name did not compile: $stderr"
  elif [[ -n "$stderr" ]]; then
    fail "$name compiled with warnings: $stderr"
  else
    pass "$name"
  fi
done

# Reads a parameter's default value out of a compiled template.
default_of() {
  local module="$1" parameter="$2"
  az bicep build --file "$infra_dir/modules/$module" --stdout 2>/dev/null \
    | python3 -c "
import json, sys
template = json.load(sys.stdin)
parameter = template.get('parameters', {}).get('$parameter')
print('' if parameter is None else parameter.get('defaultValue', ''))
"
}

echo
echo "Checking Document Intelligence tier"

di_sku="$(default_of document-intelligence.bicep skuName)"
if [[ -z "$di_sku" ]]; then
  fail "document-intelligence.bicep has no skuName default to check."
elif [[ "$di_sku" == "F0" ]]; then
  fail "Document Intelligence defaults to F0, which reads only the first two pages of
        every document and reports success. Large regulations ingest into a handful of
        chunks and retrieval silently misses the rest. Use S0."
else
  pass "Document Intelligence tier is $di_sku (not F0)"
fi

echo
echo "Checking Azure OpenAI deployment capacity"

# Sized against real workloads rather than picked round: ingestion embeds whole
# documents in tight batches, and question answering has reviewers waiting on a
# response where a 429 surfaces as a failed answer. Raising either is fine —
# GlobalStandard bills per token generated, so unused headroom is free.
#
# Written as name:minimum pairs rather than an associative array so the script
# runs on the bash 3.2 that ships with macOS as well as on the CI runner.
for requirement in chatDeploymentCapacity:100 embeddingDeploymentCapacity:250; do
  parameter="${requirement%%:*}"
  minimum="${requirement##*:}"
  capacity="$(default_of openai.bicep "$parameter")"

  if [[ -z "$capacity" ]]; then
    fail "openai.bicep has no $parameter. The chat and embedding deployments must be
          sized separately; a single shared capacity starves whichever workload needs
          more."
  elif (( capacity < minimum )); then
    fail "openai.bicep sets $parameter to $capacity, below the required $minimum
          (thousands of tokens per minute)."
  else
    pass "$parameter is $capacity (>= $minimum)"
  fi
done

echo
if (( failures > 0 )); then
  echo "$failures check(s) failed." >&2
  exit 1
fi

echo "All infrastructure checks passed."
