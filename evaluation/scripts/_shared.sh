#!/usr/bin/env bash
# Shared helpers for the evaluation runner scripts. Source it; do not run it.
#
# Cost note: every runner defaults to the offline fixture harness. Live runs
# call metered Azure services and are opt-in only, via --live.

set -euo pipefail

# Repository root, derived from this file's location.
EVALUATION_SCRIPTS_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${EVALUATION_SCRIPTS_DIR}/../.." && pwd)"
BACKEND_DIR="${REPO_ROOT}/backend"

# Credentials live outside the repository and are never committed.
AZURE_ENV_FILE="${AZURE_ENV_FILE:-${HOME}/.harriscountyai/azure.env}"

MODE="fixture"
UPDATE_BASELINE="0"

usage() {
  cat <<USAGE
Usage: $(basename "$0") [--fixture | --live] [--update]

  --fixture   Run the deterministic offline harness (default). No Azure calls,
              no cost, reproducible on any machine.
  --live      Run against the Azure services configured in ${AZURE_ENV_FILE}.
              Calls metered services; only use this deliberately.
  --update    Rewrite the committed fixture baseline instead of asserting
              against it. Review the diff before committing. Fixture mode only.
  -h, --help  Show this message.
USAGE
}

parse_evaluation_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --fixture) MODE="fixture" ;;
      --live) MODE="live" ;;
      --update) UPDATE_BASELINE="1" ;;
      -h|--help) usage; exit 0 ;;
      *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
    shift
  done

  if [[ "${MODE}" == "live" && "${UPDATE_BASELINE}" == "1" ]]; then
    echo "--update applies to the fixture baseline only; a live run writes its own result file." >&2
    exit 2
  fi
}

# Loads Azure credentials from outside the repository. Values are exported for
# the child dotnet process and are never echoed.
load_azure_credentials() {
  if [[ ! -f "${AZURE_ENV_FILE}" ]]; then
    cat >&2 <<MISSING
No Azure environment file at ${AZURE_ENV_FILE}.

A live run needs the same configuration the application uses, in the standard
double-underscore form, for example:

  Search__Endpoint=https://<service>.search.windows.net
  Search__ApiKey=<key>
  Embeddings__Endpoint=https://<resource>.openai.azure.com/
  Embeddings__ApiKey=<key>
  LanguageModel__Endpoint=https://<resource>.openai.azure.com/
  LanguageModel__ApiKey=<key>

Keep that file outside the repository. Set AZURE_ENV_FILE to use another path.
MISSING
    exit 1
  fi

  set -a
  # shellcheck disable=SC1090
  source "${AZURE_ENV_FILE}"
  set +a

  map_legacy_azure_names
}

# The credentials file predates the harness and uses short names
# (SEARCH_ENDPOINT, AOAI_KEY, …). Map them onto the double-underscore
# configuration form the application binds, without overwriting anything the
# caller set explicitly. Values are only ever assigned, never echoed.
map_legacy_azure_names() {
  export_if_unset "Search__Endpoint" "${SEARCH_ENDPOINT:-}"
  export_if_unset "Search__ApiKey" "${SEARCH_API_KEY:-${SEARCH_ADMIN_KEY:-}}"
  export_if_unset "Search__IndexName" "${SEARCH_INDEX_NAME:-}"

  export_if_unset "Embeddings__Endpoint" "${AOAI_ENDPOINT:-}"
  export_if_unset "Embeddings__ApiKey" "${AOAI_KEY:-}"
  export_if_unset "Embeddings__Deployment" "${AOAI_EMBEDDING_DEPLOYMENT:-}"

  export_if_unset "LanguageModel__Endpoint" "${AOAI_ENDPOINT:-}"
  export_if_unset "LanguageModel__ApiKey" "${AOAI_KEY:-}"
  export_if_unset "LanguageModel__Deployment" "${AOAI_CHAT_DEPLOYMENT:-}"
}

export_if_unset() {
  local name="$1"
  local value="$2"
  if [[ -n "${value}" && -z "${!name:-}" ]]; then
    export "${name}=${value}"
  fi
}

# Runs one xunit filter in the integration test project with the right gates set.
run_evaluation_tests() {
  local filter="$1"

  if [[ "${MODE}" == "live" ]]; then
    load_azure_credentials
    export RUN_EVALUATION=1
    echo "Running LIVE evaluation (${filter}). This calls metered Azure services."
  else
    unset RUN_EVALUATION || true
    export UPDATE_EVALUATION_BASELINE="${UPDATE_BASELINE}"
    echo "Running offline fixture evaluation (${filter}). No Azure calls."
  fi

  dotnet test "${BACKEND_DIR}/tests/HarrisCountyAI.IntegrationTests/HarrisCountyAI.IntegrationTests.csproj" \
    --filter "${filter}"
}
