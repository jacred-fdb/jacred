#!/usr/bin/env bash
# Status for crontab-based JacRed jobs: installed crontab + flock locks + sample env.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCK_DIR="${LOCK_DIR:-/tmp/jacred-cron-locks}"
GEN_DIR="${SCRIPT_DIR}/generated"

log() {
  echo "[check] $*"
}

echo "JacRed cron (crontab + run-job.sh)"
echo

if ! command -v crontab >/dev/null 2>&1; then
  log "ERROR: crontab not found"
  exit 1
fi

echo "== crontab (jacred run-job lines) =="
if crontab -l 2>/dev/null | grep -F 'run-job.sh' >/dev/null; then
  crontab -l 2>/dev/null | grep -E '^(#|SHELL=|PATH=|[0-9*])' | head -80
else
  log "WARN: no run-job.sh lines in crontab — run: sudo ${SCRIPT_DIR}/install.sh"
fi
echo

echo "== generated env files =="
if compgen -G "${GEN_DIR}/jacred-job-*.env" > /dev/null; then
  count="$(find "${GEN_DIR}" -maxdepth 1 -name 'jacred-job-*.env' | wc -l | tr -d ' ')"
  log "${count} env file(s) in ${GEN_DIR}"
else
  log "WARN: no ${GEN_DIR}/jacred-job-*.env — run: python3 ${SCRIPT_DIR}/generate.py"
fi
echo

echo "== flock locks (${LOCK_DIR}) =="
if [[ -d "${LOCK_DIR}" ]]; then
  # shellcheck disable=SC2012
  ls -la "${LOCK_DIR}" 2>/dev/null || log "(empty)"
  # Held locks: processes with flock on these files (best-effort).
  if command -v lsof >/dev/null 2>&1; then
    held="$(lsof +D "${LOCK_DIR}" 2>/dev/null | awk 'NR>1 {print}' || true)"
    if [[ -n "${held}" ]]; then
      echo
      echo "Currently held (lsof):"
      echo "${held}"
    else
      log "no held locks (idle)"
    fi
  fi
else
  log "lock dir not created yet (ok until first job run)"
fi
echo

echo "== leftover systemd jacred-job units (should be empty) =="
if command -v systemctl >/dev/null 2>&1; then
  units="$(systemctl list-units 'jacred-job-*' --all --no-legend 2>/dev/null | wc -l | tr -d ' ')"
  files="$(systemctl list-unit-files 'jacred-job-*' --no-legend 2>/dev/null | wc -l | tr -d ' ')"
  if [[ "${units}" != "0" || "${files}" != "0" ]]; then
    log "WARN: systemd leftovers still present — run: sudo ${SCRIPT_DIR}/uninstall.sh"
    systemctl list-units 'jacred-job-*' --all --no-legend 2>/dev/null || true
  else
    log "none"
  fi
else
  log "systemctl not available"
fi
