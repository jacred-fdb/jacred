#!/usr/bin/env bash
#
# JacRed installer
# Run from any account; will prompt for sudo if not root.
# Cron is added for the user who invoked sudo (or root if run as root).
#
set -euo pipefail

readonly SCRIPT_NAME="${0##*/}"
readonly INSTALL_ROOT="/opt/jacred"
readonly JACRED_USER="jacred"
readonly SERVICE_NAME="jacred"
readonly SYSTEMD_UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
readonly RELEASE_BASE="https://github.com/jacred-fdb/jacred/releases/latest/download"
readonly DB_URL="https://jacred.torrservera.net/latest.zip"
readonly CRON_JACRED_MARKER="127.0.0.1:9117"
readonly SAVE_URL="http://127.0.0.1:9117/jsondb/save"

CRON_USER="${SUDO_USER:-root}"
DOWNLOAD_DB=0   # 0 = skip DB download (use --no-download-db)
REMOVE=0
UPDATE=0
CLEANUP_PATHS=()
ARCH=""
PUBLISH_URL=""

log_info() {
  printf '[%s] %s\n' "$SCRIPT_NAME" "$*"
}

log_err() {
  printf '[%s] ERROR: %s\n' "$SCRIPT_NAME" "$*" >&2
}

usage() {
  cat << EOF
Usage: $SCRIPT_NAME [OPTIONS]

Install, update, or remove JacRed. Run as any user; sudo will be used when needed.

Options:
  --no-download-db    Do not download or unpack the initial database (install only)
  --update            Update app from latest release (saves DB, replaces files, restarts)
  --remove            Fully remove JacRed (service, cron, app directory)
  -h, --help          Show this help and exit

Examples:
  $SCRIPT_NAME
  $SCRIPT_NAME --no-download-db
  $SCRIPT_NAME --update
  $SCRIPT_NAME --remove

Run as a specific user (cron added/removed for that user):
  sudo -u myservice $SCRIPT_NAME
  sudo -u myservice $SCRIPT_NAME --update
  sudo -u myservice $SCRIPT_NAME --remove
EOF
}

cleanup() {
  local path
  for path in "${CLEANUP_PATHS[@]}"; do
    if [[ -e "$path" ]]; then
      log_info "Removing temporary path: $path"
      rm -rf "$path"
    fi
  done
}

detect_arch() {
  case "$(uname -m)" in
    x86_64)   echo "amd64" ;;
    aarch64|arm64) echo "arm64" ;;
    *)
      log_err "Unsupported architecture: $(uname -m). Supported: linux-amd64, linux-arm64."
      exit 1
      ;;
  esac
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -h|--help)
        usage
        exit 0
        ;;
      --no-download-db)
        DOWNLOAD_DB=0
        shift
        ;;
      --remove)
        REMOVE=1
        shift
        ;;
      --update)
        UPDATE=1
        shift
        ;;
      *)
        log_err "Unknown option: $1"
        usage >&2
        exit 1
        ;;
    esac
  done
}

require_root() {
  if [[ ${EUID} -ne 0 ]]; then
    exec sudo "$0" "$@"
  fi
}

# Run a command as CRON_USER (root or via su).
run_as_cron_user() {
  if [[ "$CRON_USER" == "root" ]]; then
    "$@"
  else
    su "$CRON_USER" -c "$(printf '%q ' "$@")"
  fi
}

remove_service() {
  if [[ ! -f "$SYSTEMD_UNIT_PATH" ]]; then
    log_info "Service unit not found, skipping"
    return 0
  fi
  log_info "Stopping and disabling $SERVICE_NAME service..."
  systemctl stop "$SERVICE_NAME" 2>/dev/null || true
  systemctl disable "$SERVICE_NAME" 2>/dev/null || true
  rm -f "$SYSTEMD_UNIT_PATH"
  systemctl daemon-reload
  log_info "Service removed"
}

remove_cron() {
  local current filtered
  current="$(run_as_cron_user crontab -l 2>/dev/null)" || current=""
  if [[ "$current" != *"$CRON_JACRED_MARKER"* ]]; then
    log_info "No jacred cron jobs found for $CRON_USER, skipping"
    return 0
  fi
  log_info "Removing jacred cron jobs for user: $CRON_USER"
  filtered="$(printf '%s\n' "$current" | grep -vF "$CRON_JACRED_MARKER" || true)"
  printf '%s\n' "$filtered" | run_as_cron_user crontab -
  log_info "Cron removed"
}

remove_app() {
  if [[ ! -d "$INSTALL_ROOT" ]]; then
    log_info "Install directory not found: $INSTALL_ROOT, skipping"
    return 0
  fi
  log_info "Removing install directory: $INSTALL_ROOT"
  rm -rf "$INSTALL_ROOT"
  log_info "App directory removed"
}

do_remove() {
  log_info "Starting full removal..."
  remove_service
  remove_cron
  remove_app
  log_info "Removal complete."
}

do_update() {
  if [[ ! -d "$INSTALL_ROOT" ]]; then
    log_err "Install directory not found: $INSTALL_ROOT. Install first."
    exit 1
  fi
  ensure_service_user
  log_info "Saving database..."
  curl -s "$SAVE_URL" || log_info "Save request sent (service may be stopped)"
  log_info "Stopping $SERVICE_NAME service..."
  systemctl stop "$SERVICE_NAME" 2>/dev/null || true
  log_info "Downloading latest release..."
  cd "$INSTALL_ROOT"
  if ! wget -q "$PUBLISH_URL" -O publish.zip || [[ ! -s publish.zip ]]; then
    log_err "Download failed: $PUBLISH_URL"
    exit 1
  fi
  log_info "Unpacking..."
  unzip -oq publish.zip
  rm -f publish.zip
  chmod +x "${INSTALL_ROOT}/JacRed"
  set_install_ownership
  log_info "Starting $SERVICE_NAME service..."
  systemctl start "$SERVICE_NAME"
  log_info "Update complete."
}

install_apt_packages() {
  log_info "Installing system packages (wget, unzip)..."
  apt update
  apt install -y --no-install-recommends wget unzip
}

ensure_service_user() {
  if getent passwd "$JACRED_USER" &>/dev/null; then
    log_info "System user $JACRED_USER already exists"
    return 0
  fi
  log_info "Creating system user: $JACRED_USER"
  useradd --system --no-create-home --shell /usr/sbin/nologin "$JACRED_USER"
}

set_install_ownership() {
  log_info "Setting ownership to $JACRED_USER:$JACRED_USER"
  chown -R "${JACRED_USER}:${JACRED_USER}" "$INSTALL_ROOT"
}

# --- Install ---

install_app() {
  log_info "Downloading and extracting application (jacred-linux-${ARCH}.zip)..."
  mkdir -p "$INSTALL_ROOT"
  cd "$INSTALL_ROOT"
  if ! wget -q "$PUBLISH_URL" -O publish.zip || [[ ! -s publish.zip ]]; then
    log_err "Download failed: $PUBLISH_URL"
    exit 1
  fi
  unzip -oq publish.zip
  rm -f publish.zip
  chmod +x "${INSTALL_ROOT}/JacRed"
  cp -n "${INSTALL_ROOT}/Data/init.conf" "${INSTALL_ROOT}/init.conf" 2>/dev/null || true
  log_info "Application installed to $INSTALL_ROOT"
}

install_systemd_unit() {
  log_info "Installing systemd unit: $SYSTEMD_UNIT_PATH"
  cat << EOF > "$SYSTEMD_UNIT_PATH"
[Unit]
Description=$SERVICE_NAME
Wants=network.target
After=network.target
[Service]
User=$JACRED_USER
Group=$JACRED_USER
WorkingDirectory=$INSTALL_ROOT
ExecStart=$INSTALL_ROOT/JacRed
Restart=always
[Install]
WantedBy=multi-user.target
EOF
  chmod 644 "$SYSTEMD_UNIT_PATH"
  systemctl daemon-reload
  systemctl enable "$SERVICE_NAME"
}

install_cron() {
  local crontab_file="${INSTALL_ROOT}/Data/crontab"
  if [[ ! -f "$crontab_file" ]]; then
    log_info "Data/crontab not found, skipping crontab install"
    return 0
  fi
  log_info "Installing crontab from Data/crontab for user: $CRON_USER"
  run_as_cron_user crontab "$crontab_file"
  log_info "Crontab installed"
}

install_database() {
  if [[ "$DOWNLOAD_DB" -ne 1 ]]; then
    log_info "Skipping database download (--no-download-db)"
    return 0
  fi
  log_info "Downloading database..."
  cd "$INSTALL_ROOT"
  if ! wget -q "$DB_URL" -O latest.zip || [[ ! -s latest.zip ]]; then
    log_err "Database download failed: $DB_URL"
    exit 1
  fi
  log_info "Unpacking database..."
  unzip -oq latest.zip
  rm -f latest.zip
  log_info "Database installed"
}

start_service() {
  log_info "Starting $SERVICE_NAME service..."
  systemctl start "$SERVICE_NAME"
}

print_post_install() {
  cat << EOF

################################################################

Installation complete.

  - Edit config: $INSTALL_ROOT/init.conf
  - Restart:     systemctl restart $SERVICE_NAME
  - Full crontab: crontab $INSTALL_ROOT/Data/crontab

################################################################

EOF
}

main() {
  trap cleanup EXIT
  require_root "$@"
  parse_args "$@"

  if [[ "$(uname -s)" != "Linux" ]]; then
    log_err "This script supports Linux only. Use the release assets for your platform."
    exit 1
  fi
  ARCH=$(detect_arch)
  PUBLISH_URL="${RELEASE_BASE}/jacred-linux-${ARCH}.zip"
  log_info "Using release asset: jacred-linux-${ARCH}.zip"

  if [[ "$REMOVE" -eq 1 ]]; then
    do_remove
    return 0
  fi

  if [[ "$UPDATE" -eq 1 ]]; then
    do_update
    return 0
  fi

  log_info "Starting installation..."

  install_apt_packages
  ensure_service_user
  install_app
  install_systemd_unit
  install_cron
  install_database
  set_install_ownership
  start_service
  print_post_install
}

main "$@"
