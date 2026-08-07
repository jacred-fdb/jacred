#!/usr/bin/env bash
# Remove all JacRed systemd job timers/services leftover from the old scheduler.
set -euo pipefail

SYSTEMD_DIR="${SYSTEMD_DIR:-/etc/systemd/system}"
MANAGED_DIR="${MANAGED_DIR:-/etc/systemd/jacred-cron}"
JOBS_TARGET="${JOBS_TARGET:-jacred-jobs.target}"

log() {
  echo "[uninstall] $*"
}

if ! command -v systemctl >/dev/null 2>&1; then
  log "systemctl not found — nothing to remove"
  exit 0
fi

if [[ "$(id -u)" -ne 0 ]]; then
  SUDO="sudo"
else
  SUDO=""
fi

log "stopping / disabling ${JOBS_TARGET}"
${SUDO} systemctl disable --now "${JOBS_TARGET}" 2>/dev/null || true

mapfile -t UNITS < <(
  {
    if [[ -d "${MANAGED_DIR}" ]]; then
      find "${MANAGED_DIR}" -maxdepth 1 \( -name 'jacred-job-*.timer' -o -name 'jacred-job-*.service' -o -name 'jacred-jobs.target' \) -printf '%f\n' 2>/dev/null || true
    fi
    find "${SYSTEMD_DIR}" -maxdepth 1 \( -name 'jacred-job-*.timer' -o -name 'jacred-job-*.service' -o -name 'jacred-jobs.target' \) -printf '%f\n' 2>/dev/null || true
    systemctl list-unit-files 'jacred-job-*' --no-legend 2>/dev/null | awk '{print $1}' || true
    systemctl list-units 'jacred-job-*' --all --no-legend 2>/dev/null | awk '{print $1}' || true
    systemctl list-units 'jacred-jobs.target' --all --no-legend 2>/dev/null | awk '{print $1}' || true
  } | sed 's/[●*]//g' | grep -E '^jacred-(job-|jobs\.target)' | sort -u
)

log "found ${#UNITS[@]} unit name(s) to remove"

for unit in "${UNITS[@]}"; do
  [[ -z "${unit}" ]] && continue
  log "  disable/stop ${unit}"
  ${SUDO} systemctl disable --now "${unit}" 2>/dev/null || true
  ${SUDO} systemctl stop "${unit}" 2>/dev/null || true
done

log "removing unit files / symlinks from ${SYSTEMD_DIR}"
for f in "${SYSTEMD_DIR}"/jacred-job-*.service "${SYSTEMD_DIR}"/jacred-job-*.timer "${SYSTEMD_DIR}"/jacred-jobs.target; do
  if [[ -e "${f}" || -L "${f}" ]]; then
    log "  rm ${f}"
    ${SUDO} rm -f "${f}"
  fi
done

if [[ -d "${MANAGED_DIR}" ]]; then
  log "removing managed dir ${MANAGED_DIR}"
  ${SUDO} rm -rf "${MANAGED_DIR}"
fi

log "daemon-reload + reset-failed"
${SUDO} systemctl daemon-reload
${SUDO} systemctl reset-failed 'jacred-job-*' 2>/dev/null || true
${SUDO} systemctl reset-failed "${JOBS_TARGET}" 2>/dev/null || true

leftover="$(systemctl list-units 'jacred-job-*' --all --no-legend 2>/dev/null | wc -l | tr -d ' ')"
leftover_files="$(systemctl list-unit-files 'jacred-job-*' --no-legend 2>/dev/null | wc -l | tr -d ' ')"
log "remaining list-units jacred-job-*: ${leftover}"
log "remaining list-unit-files jacred-job-*: ${leftover_files}"

if [[ "${leftover}" != "0" || "${leftover_files}" != "0" ]]; then
  log "WARN: some units still listed — inspect with systemctl list-units 'jacred-job-*' --all"
  exit 1
fi

log "done — all jacred-job systemd units removed"
