#!/usr/bin/env bash
# Generate job env + safe crontab, remove leftover systemd timers, install crontab.
set -euo pipefail

CRON_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${CRON_DIR}/.." && pwd)"
CRONTAB_FILE="${CRONTAB_FILE:-${ROOT_DIR}/Data/crontab}"

log() {
  echo "[install] $*"
}

if [[ "$(id -u)" -ne 0 ]]; then
  SUDO="sudo"
else
  SUDO=""
fi

log "generating .env + crontab from jobs.yaml"
python3 "${CRON_DIR}/generate.py" --cron-dir "${CRON_DIR}"

chmod +x "${CRON_DIR}/run-job.sh" "${CRON_DIR}/uninstall.sh" "${CRON_DIR}/check-jobs.sh"

GEN="${CRON_DIR}/generated"
if ! compgen -G "${GEN}/jacred-job-*.env" > /dev/null; then
  log "ERROR: no job env files generated"
  exit 1
fi
if [[ ! -f "${CRONTAB_FILE}" ]]; then
  log "ERROR: crontab file not found: ${CRONTAB_FILE}"
  exit 1
fi

# Always strip leftover systemd JacRed timers (crontab is the scheduler now).
if [[ -x "${CRON_DIR}/uninstall.sh" ]]; then
  log "removing leftover jacred-job systemd units (if any)"
  "${CRON_DIR}/uninstall.sh" || log "WARN: uninstall reported leftovers (ok if none were installed)"
fi

log "installing host crontab from ${CRONTAB_FILE}"
crontab "${CRONTAB_FILE}"

log "done"
log "  crontab -l | head"
log "  ${CRON_DIR}/check-jobs.sh"
log "  manual run: ${CRON_DIR}/run-job.sh rutor-parse"
