#!/usr/bin/env bash
# Run one JacRed HTTP job (crontab or manual).
# Usage: run-job.sh <name> <url> <max_time>
# JacRed returns ok / work / disabled. Long jobs may start work in the
# background and return immediately; curl still has an overall --max-time.
# Overlap protection: flock on LOCK_DIR/<job>.lock.
set -euo pipefail

JOB_NAME="${1:?usage: run-job.sh <name> <url> <max_time>}"
JOB_URL="${2:?usage: run-job.sh <name> <url> <max_time>}"
MAX_TIME="${3:?usage: run-job.sh <name> <url> <max_time>}"
LOCK_DIR="${LOCK_DIR:-/tmp/jacred-cron-locks}"
CONNECT_TIMEOUT="${CURL_CONNECT_TIMEOUT:-10}"

log() {
  echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"
}

if ! [[ "$MAX_TIME" =~ ^[1-9][0-9]*$ ]]; then
  log "ERROR: max_time must be a positive integer, got: ${MAX_TIME}"
  exit 1
fi

mkdir -p "$LOCK_DIR"
LOCK_FILE="${LOCK_DIR}/${JOB_NAME}.lock"
exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  log "SKIP ${JOB_NAME}: previous run still active"
  exit 0
fi

log "START ${JOB_NAME} url=${JOB_URL} max_time=${MAX_TIME}s"
start_ts="$(date +%s)"

# Body on stdout; HTTP code via -w. Overall --max-time prevents infinite hangs.
tmp="$(mktemp)"
trap 'rm -f "$tmp" "${tmp}.code" "${tmp}.err"' EXIT

http_code="000"
curl_rc=0
if curl -sS --connect-timeout "$CONNECT_TIMEOUT" --max-time "$MAX_TIME" \
  -o "$tmp" -w '%{http_code}' "$JOB_URL" >"${tmp}.code" 2>"${tmp}.err"; then
  http_code="$(tr -d '\n' <"${tmp}.code")"
  body="$(tr -d '\r' <"$tmp")"
else
  curl_rc=$?
  err="$(tr '\n' ' ' <"${tmp}.err" 2>/dev/null || true)"
  http_code="$(tr -d '\n' <"${tmp}.code" 2>/dev/null || echo 000)"
  # curl exit 28 = operation timeout
  if [[ "$curl_rc" -eq 28 ]]; then
    body="TIMEOUT after ${MAX_TIME}s${err:+: ${err}}"
  else
    body="curl-error${err:+: ${err}}"
  fi
fi

body="${body//$'\n'/ }"
body="${body#"${body%%[![:space:]]*}"}"
body="${body%"${body##*[![:space:]]}"}"

elapsed="$(( $(date +%s) - start_ts ))"

case "$body" in
  TIMEOUT*)
    log "TIMEOUT ${JOB_NAME}: ${body} (http=${http_code} ${elapsed}s)"
    exit 1
    ;;
  curl-error*)
    log "DONE  ${JOB_NAME}: ${body} (http=${http_code} ${elapsed}s)"
    exit 1
    ;;
esac

log "DONE  ${JOB_NAME}: ${body:-empty} (http=${http_code} ${elapsed}s)"
exit 0
