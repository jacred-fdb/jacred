#!/usr/bin/env bash
# Limited live Rutracker smoke via FlareSolverr — all three cron jobs, capped.
#
# Covers:
#   1) /cron/cloudflare/Warmup
#   2) /cron/rutracker/parse            (one cat, maxTopics)
#   3) /cron/rutracker/UpdateTasksParse (one cat — NOT all 211 forums)
#   4) /cron/rutracker/ParseAllTask     (one cat, maxPages — NOT full backlog)
#
# Background jobs are polled via /health/background-jobs (re-calling cron
# endpoints would start a new run).
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
MAX_PAGES="${MAX_PAGES:-1}"
POLL_SEC="${POLL_SEC:-2}"
WAIT_BG="${WAIT_BG:-180}"

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

# Wait until jobKey (e.g. rutracker:ParseAllTask) leaves /health/background-jobs.
wait_bg_job() {
  local job_key="$1"
  local deadline=$((SECONDS + WAIT_BG))
  local seen=0
  while (( SECONDS < deadline )); do
    local jobs
    jobs="$(curl -fsS --max-time 15 "$(admin_url /health/background-jobs)" || echo '[]')"
    if [[ "$jobs" == *"$job_key"* ]]; then
      seen=1
      echo "  … $job_key still running"
      sleep "$POLL_SEC"
      continue
    fi
    if (( seen == 1 )); then
      echo "  $job_key finished"
      return 0
    fi
    # Job may finish before first poll — treat as done after a brief settle.
    sleep "$POLL_SEC"
    jobs="$(curl -fsS --max-time 15 "$(admin_url /health/background-jobs)" || echo '[]')"
    if [[ "$jobs" != *"$job_key"* ]]; then
      echo "  $job_key finished (or was very fast)"
      return 0
    fi
  done
  echo "FAIL: $job_key still active after ${WAIT_BG}s" >&2
  return 1
}

echo "cron_rutracker_smoke BASE_URL=$BASE_URL TIMEOUT=${TIMEOUT}s cat=$CAT maxTopics=$MAX_TOPICS maxPages=$MAX_PAGES"

warm_url="$(admin_url /cron/cloudflare/Warmup)"
echo "→ 1/4 warmup $warm_url"
warm_out="$(curl -fsS --max-time "$TIMEOUT" "$warm_url" || true)"
echo "  $warm_out"
if [[ "$warm_out" != *'"ok":true'* && "$warm_out" != *'"ok": true'* ]]; then
  echo "FAIL: warmup did not return ok:true" >&2
  exit 1
fi

parse_url="$(admin_url "/cron/rutracker/parse?page=0&cat=${CAT}&maxTopics=${MAX_TOPICS}")"
echo "→ 2/4 parse $parse_url"
parse_out="$(curl -fsS --max-time "$TIMEOUT" "$parse_url" || true)"
echo "  $parse_out"
if [[ "$parse_out" != *"${CAT} - 0 - True"* && "$parse_out" != *"${CAT} - 0 - true"* ]]; then
  echo "FAIL: expected '${CAT} - 0 - True' in parse output" >&2
  exit 1
fi

update_url="$(admin_url "/cron/rutracker/UpdateTasksParse?cat=${CAT}")"
echo "→ 3/4 UpdateTasksParse $update_url"
update_ack="$(curl -fsS --max-time 30 "$update_url" || true)"
echo "  ack: $update_ack"
if [[ "$update_ack" != "ok" && "$update_ack" != "work" ]]; then
  echo "FAIL: UpdateTasksParse unexpected ack: $update_ack" >&2
  exit 1
fi
wait_bg_job "rutracker:UpdateTasksParse"

parse_all_url="$(admin_url "/cron/rutracker/ParseAllTask?cat=${CAT}&maxPages=${MAX_PAGES}")"
echo "→ 4/4 ParseAllTask $parse_all_url"
all_ack="$(curl -fsS --max-time 30 "$parse_all_url" || true)"
echo "  ack: $all_ack"
if [[ "$all_ack" != "ok" && "$all_ack" != "work" ]]; then
  echo "FAIL: ParseAllTask unexpected ack: $all_ack" >&2
  exit 1
fi
wait_bg_job "rutracker:ParseAllTask"

echo "PASS: rutracker smoke ok (parse + UpdateTasksParse + ParseAllTask, capped)"
