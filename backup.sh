#!/usr/bin/env bash
#
# Daily hardlink snapshot and zip backup for JacRed
# Creates a space-efficient backup using hardlinks and generates latest.tar.zst
#
# Usage:
#   ./backup.sh [OPTIONS]
#
# Options:
#   --fastest     Minimal compression, maximum speed (~15-20 sec)
#   --fast        Fast compression (default, ~30-40 sec)
#   --balanced    Balanced speed/size (~1-2 min)
#   --smallest    Maximum compression, smallest size (~30 min)
#
# Cron examples (run daily at 02:00):
#   From any user's crontab (auto-switches to directory owner via sudo):
#     0 2 * * * /opt/jacred/backup.sh
#     0 2 * * * /opt/jacred/backup.sh --fastest
#
# The script auto-detects the owner of /opt/jacred and runs as that user.
# Override with: JACRED_USER=myuser ./backup.sh
#
set -euo pipefail

readonly SCRIPT_NAME="${0##*/}"
readonly INSTALL_ROOT="${JACRED_ROOT:-/opt/jacred}"
readonly BACKUP_DIR="${INSTALL_ROOT}/backup"
readonly DATA_DIR="${INSTALL_ROOT}/Data"
readonly WWWROOT_DIR="${INSTALL_ROOT}/wwwroot"

get_owner() {
  stat -c '%U' "$INSTALL_ROOT" 2>/dev/null || stat -f '%Su' "$INSTALL_ROOT"
}

JACRED_USER="${JACRED_USER:-$(get_owner)}"
ZSTD_LEVEL="--fast"

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --fastest)
        ZSTD_LEVEL="--fast=10"
        shift
        ;;
      --fast)
        ZSTD_LEVEL="--fast"
        shift
        ;;
      --balanced)
        ZSTD_LEVEL="-1"
        shift
        ;;
      --smallest)
        ZSTD_LEVEL="-19"
        shift
        ;;
      -h|--help)
        head -n 22 "$0" | tail -n +2 | sed 's/^#//' | sed 's/^ //'
        exit 0
        ;;
      *)
        log_err "Unknown option: $1"
        exit 1
        ;;
    esac
  done
}

log_info() {
  printf '[%s] %s\n' "$SCRIPT_NAME" "$*"
}

log_err() {
  printf '[%s] ERROR: %s\n' "$SCRIPT_NAME" "$*" >&2
}

check_user() {
  local current_user
  current_user="$(id -un)"
  if [[ "$current_user" == "$JACRED_USER" ]]; then
    return 0
  fi
  if [[ "$current_user" == "root" ]]; then
    log_info "Running as root, switching to $JACRED_USER..."
    exec su -s /bin/bash "$JACRED_USER" -c "$0"
  fi
  log_info "Running as $current_user, switching to $JACRED_USER via sudo..."
  exec sudo -u "$JACRED_USER" "$0"
}

validate_paths() {
  if [[ ! -d "$DATA_DIR/fdb" ]]; then
    log_err "Source directory not found: $DATA_DIR/fdb"
    exit 1
  fi
  if [[ ! -f "$DATA_DIR/masterDb.bz" ]]; then
    log_err "masterDb.bz not found: $DATA_DIR/masterDb.bz"
    exit 1
  fi
}

rotate_backup() {
  log_info "Rotating previous backup..."
  rm -rf "${BACKUP_DIR}/daily.prev"
  mv "${BACKUP_DIR}/daily" "${BACKUP_DIR}/daily.prev" 2>/dev/null || true
}

create_hardlink_snapshot() {
  log_info "Creating hardlink snapshot..."
  mkdir -p "${BACKUP_DIR}/daily"
  cp -al "${DATA_DIR}/fdb" "${BACKUP_DIR}/daily/"
  cp -p "${DATA_DIR}/masterDb.bz" "${BACKUP_DIR}/daily/"
}

create_archive() {
  log_info "Creating archive..."
  mkdir -p "$WWWROOT_DIR"

  log_info "Using zstd (multi-threaded, level: $ZSTD_LEVEL)..."
  tar -C "${BACKUP_DIR}/daily" -cf - . | zstd -T0 $ZSTD_LEVEL -o "${WWWROOT_DIR}/latest.tar.zst.tmp"

  mv "${WWWROOT_DIR}/latest.tar.zst.tmp" "${WWWROOT_DIR}/latest.tar.zst"
  log_info "Archive created: ${WWWROOT_DIR}/latest.tar.zst"
}

main() {
  parse_args "$@"
  check_user
  log_info "Starting daily backup..."
  cd "$INSTALL_ROOT"

  validate_paths
  rotate_backup
  create_hardlink_snapshot
  create_archive

  log_info "Backup complete."
}

main "$@"
