#!/usr/bin/env bash
# Generate systemd units from jobs.yaml and install/enable timers.
set -euo pipefail

CRON_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SYSTEMD_DIR="${SYSTEMD_DIR:-/etc/systemd/system}"
MANAGED_DIR="${MANAGED_DIR:-/etc/systemd/jacred-cron}"

log() {
  echo "[install] $*"
}

log "generating units from jobs.yaml"
python3 "${CRON_DIR}/generate.py" --cron-dir "${CRON_DIR}"

chmod +x "${CRON_DIR}/run-job.sh"

GEN="${CRON_DIR}/generated"
if ! compgen -G "${GEN}/jacred-job-*.timer" > /dev/null; then
  log "ERROR: no timer units generated"
  exit 1
fi

if [[ "$(id -u)" -ne 0 ]]; then
  SUDO="sudo"
else
  SUDO=""
fi

# Disable legacy loop scheduler if present
if command -v systemctl >/dev/null 2>&1; then
  ${SUDO} systemctl disable --now jacred-scheduler.service 2>/dev/null || true
fi

log "staging units in ${MANAGED_DIR}"
${SUDO} mkdir -p "${MANAGED_DIR}"
${SUDO} cp "${GEN}/"*.service "${GEN}/"*.timer "${GEN}/"jacred-jobs.target "${MANAGED_DIR}/"

log "linking units into ${SYSTEMD_DIR}"
for stale in "${SYSTEMD_DIR}"/jacred-job-*.service "${SYSTEMD_DIR}"/jacred-job-*.timer "${SYSTEMD_DIR}"/jacred-jobs.target; do
  [[ -e "${stale}" || -L "${stale}" ]] && ${SUDO} rm -f "${stale}"
done

for unit in "${MANAGED_DIR}"/jacred-job-*.service "${MANAGED_DIR}"/jacred-job-*.timer "${MANAGED_DIR}"/jacred-jobs.target; do
  ${SUDO} ln -sfn "${unit}" "${SYSTEMD_DIR}/$(basename "${unit}")"
done

log "daemon-reload"
${SUDO} systemctl daemon-reload

log "enable jacred-jobs.target"
${SUDO} systemctl enable --now jacred-jobs.target

log "done — list timers: systemctl list-timers 'jacred-job-*'"
log "migrate off crontab: crontab -l | grep -vF '127.0.0.1:9117' | crontab -"
