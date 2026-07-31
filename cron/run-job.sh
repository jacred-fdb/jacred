#!/usr/bin/env bash
# Run one JacRed HTTP job (invoked by systemd oneshot service).
# JacRed returns ok / work / disabled; long jobs (ParseAllTask) hold the HTTP
# connection until finished — that is expected.
set -euo pipefail

JOB_NAME="${1:?job name required}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="${SCRIPT_DIR}/generated/jacred-job-${JOB_NAME}.env"
LOCK_DIR="${LOCK_DIR:-/tmp/jacred-cron-locks}"
CONNECT_TIMEOUT="${CURL_CONNECT_TIMEOUT:-10}"

log() {
  echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"
}

if [[ ! -f "$ENV_FILE" ]]; then
  log "ERROR: env file not found: $ENV_FILE"
  exit 1
fi

# shellcheck source=/dev/null
source "$ENV_FILE"

mkdir -p "$LOCK_DIR"
LOCK_FILE="${LOCK_DIR}/${JOB_NAME}.lock"
exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  log "SKIP ${JOB_NAME}: previous run still active"
  exit 0
fi

log "START ${JOB_NAME} url=${JOB_URL}"
start_ts="$(date +%s)"

# Body on stdout; HTTP code appended as last line via -w.
# No overall -m timeout: ParseAllTask / UpdateTasksParse can run a long time.
# Connect timeout only — fail fast if JacRed is down.
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

http_code="000"
if curl -sS --connect-timeout "$CONNECT_TIMEOUT" -o "$tmp" -w '%{http_code}' "$JOB_URL" >"${tmp}.code" 2>"${tmp}.err"; then
  http_code="$(tr -d '\n' <"${tmp}.code")"
  body="$(tr -d '\r' <"$tmp")"
else
  err="$(tr '\n' ' ' <"${tmp}.err" 2>/dev/null || true)"
  body="curl-error${err:+: ${err}}"
  http_code="$(tr -d '\n' <"${tmp}.code" 2>/dev/null || echo 000)"
fi

body="${body//$'\n'/ }"
body="${body#"${body%%[![:space:]]*}"}"
body="${body%"${body##*[![:space:]]}"}"

elapsed="$(( $(date +%s) - start_ts ))"
log "DONE  ${JOB_NAME}: ${body:-empty} (http=${http_code} ${elapsed}s)"

# Non-zero only on hard curl failure (JacRed work/ok/disabled are success).
case "$body" in
  curl-error*) exit 1 ;;
esac
exit 0
