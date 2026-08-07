#!/usr/bin/env bash
# Limited live Rutracker parse smoke via FlareSolverr.
# Prerequisites: JacRed running, FlareSolverr up (e.g. http://127.0.0.1:8191).
#
# Usage:
#   ./scripts/cron_rutracker_smoke.sh
#   BASE_URL=http://127.0.0.1:9117 DEVKEY=secret TIMEOUT=180 ./scripts/cron_rutracker_smoke.sh
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:9117}"
DEVKEY="${DEVKEY:-}"
TIMEOUT="${TIMEOUT:-180}"
CAT="${CAT:-2090}"
MAX_TOPICS="${MAX_TOPICS:-3}"

admin_url() {
  local path="$1"
  if [[ -n "$DEVKEY" ]]; then
    if [[ "$path" == *\?* ]]; then
      echo "${BASE_URL}${path}&devkey=${DEVKEY}"
    else
      echo "${BASE_URL}${path}?devkey=${DEVKEY}"
    fi
  else
    echo "${BASE_URL}${path}"
  fi
}

echo "cron_rutracker_smoke BASE_URL=$BASE_URL TIMEOUT=${TIMEOUT}s cat=$CAT maxTopics=$MAX_TOPICS"

warm_url="$(admin_url /cron/cloudflare/Warmup)"
echo "→ warmup $warm_url"
warm_out="$(curl -fsS --max-time "$TIMEOUT" "$warm_url" || true)"
echo "  $warm_out"
if [[ "$warm_out" != *'"ok":true'* && "$warm_out" != *'"ok": true'* ]]; then
  echo "FAIL: warmup did not return ok:true" >&2
  exit 1
fi

parse_url="$(admin_url "/cron/rutracker/parse?page=0&cat=${CAT}&maxTopics=${MAX_TOPICS}")"
echo "→ parse $parse_url"
parse_out="$(curl -fsS --max-time "$TIMEOUT" "$parse_url" || true)"
echo "  $parse_out"

if [[ "$parse_out" != *"${CAT} - 0 - True"* && "$parse_out" != *"${CAT} - 0 - true"* ]]; then
  echo "FAIL: expected '${CAT} - 0 - True' in parse output" >&2
  exit 1
fi

echo "PASS: rutracker smoke ok"
