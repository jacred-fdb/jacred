#!/usr/bin/env bash
# Stop running JacRed oneshot jobs and restart all jacred-job timers.
set -euo pipefail

MANAGED_DIR="${MANAGED_DIR:-/etc/systemd/jacred-cron}"
JOBS_TARGET="${JOBS_TARGET:-jacred-jobs.target}"

log() {
  echo "[restart] $*"
}

if ! command -v systemctl >/dev/null 2>&1; then
  echo "systemctl not found" >&2
  exit 1
fi

if [[ "$(id -u)" -ne 0 ]]; then
  SUDO="sudo"
else
  SUDO=""
fi

list_units() {
  local pattern="$1"
  if [[ -d "$MANAGED_DIR" ]]; then
    shopt -s nullglob
    local f
    for f in "${MANAGED_DIR}"/${pattern}; do
      basename "$f"
    done
    return 0
  fi
  systemctl list-unit-files "${pattern}" --no-legend 2>/dev/null | awk '{print $1}' || true
}

mapfile -t TIMERS < <(list_units 'jacred-job-*.timer' | sort -u)
mapfile -t SERVICES < <(list_units 'jacred-job-*.service' | sort -u)

if [[ "${#TIMERS[@]}" -eq 0 ]]; then
  log "ERROR: no jacred-job-*.timer units found (checked ${MANAGED_DIR})"
  exit 1
fi

log "stopping ${#SERVICES[@]} oneshot services (kills stuck curls)"
if [[ "${#SERVICES[@]}" -gt 0 ]]; then
  ${SUDO} systemctl stop "${SERVICES[@]}" 2>/dev/null || true
fi

log "restarting ${#TIMERS[@]} timers"
${SUDO} systemctl restart "${TIMERS[@]}"

log "restarting ${JOBS_TARGET}"
${SUDO} systemctl restart "${JOBS_TARGET}" 2>/dev/null || ${SUDO} systemctl start "${JOBS_TARGET}"

log "done — ${#TIMERS[@]} timers restarted"
log "check: ${0%/*}/check-jobs.sh  (or: systemctl list-timers 'jacred-job-*')"
