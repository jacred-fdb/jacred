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
readonly DB_URL="https://sync.jacred.stream/latest.tar.zst.zip"
readonly CRON_JACRED_MARKER="127.0.0.1:9117"
readonly SAVE_URL="http://127.0.0.1:9117/jsondb/save"

CRON_USER="${SUDO_USER:-root}"
DOWNLOAD_DB=0   # 0 = skip DB download (use --no-download-db)
REMOVE=0
UPDATE=0
PRE_RELEASE=0
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
  --pre-release       Install or update from latest pre-release (e.g. 2.0.0-dev1)
  --update            Update app from latest release (saves DB, replaces files, restarts)
  --remove            Fully remove JacRed (service, cron, app directory)
  -h, --help          Show this help and exit

Examples:
  $SCRIPT_NAME
  $SCRIPT_NAME --no-download-db
  $SCRIPT_NAME --update
  $SCRIPT_NAME --pre-release
  $SCRIPT_NAME --update --pre-release
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

get_prerelease_url() {
  local asset_name="jacred-linux-${ARCH}.zip"
  local api_url="https://api.github.com/repos/jacred-fdb/jacred/releases"
  local url
  url=$(python3 -c "
import json, sys, urllib.request
try:
    req = urllib.request.Request('$api_url', headers={'Accept': 'application/vnd.github+json'})
    data = json.loads(urllib.request.urlopen(req).read())
except Exception as e:
    sys.stderr.write(str(e))
    sys.exit(2)
for r in data:
    if r.get('prerelease'):
        for a in r.get('assets', []):
            if a.get('name') == '$asset_name':
                print(a['browser_download_url'])
                sys.exit(0)
sys.exit(1)
" 2>/dev/null) || {
    log_err "Could not find pre-release asset $asset_name (python3 required for --pre-release). Check GitHub and network."
    exit 1
  }
  PUBLISH_URL="$url"
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
      --pre-release)
        PRE_RELEASE=1
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
  log_info "Installing system packages (wget, zstd)..."
  apt update
  apt install -y --no-install-recommends wget zstd
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
  log_info "Adding jacred cron jobs to existing crontab for user: $CRON_USER"
  local current filtered new_crontab
  current="$(run_as_cron_user crontab -l 2>/dev/null)" || current=""
  filtered="$(printf '%s\n' "$current" | grep -vF "$CRON_JACRED_MARKER" || true)"
  if [[ -n "${filtered//[$'\n\r\t ']}" ]]; then
    new_crontab="${filtered}"$'\n'"$(cat "$crontab_file")"
  else
    new_crontab="$(cat "$crontab_file")"
  fi
  printf '%s\n' "$new_crontab" | run_as_cron_user crontab -
  log_info "Crontab updated (existing entries preserved)"
}

install_database() {
  if [[ "$DOWNLOAD_DB" -ne 1 ]]; then
    log_info "Skipping database download (--no-download-db)"
    return 0
  fi
  log_info "Downloading database..."
  cd "$INSTALL_ROOT"
  if ! wget -q "$DB_URL" -O latest.tar.zst || [[ ! -s latest.tar.zst ]]; then
    log_err "Database download failed: $DB_URL"
    exit 1
  fi
  log_info "Unpacking database..."
  mkdir -p Data
  zstd -d latest.tar.zst -c | tar -xf - -C Data
  rm -f latest.tar.zst
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
  if [[ "$PRE_RELEASE" -eq 1 ]]; then
    log_info "Resolving latest pre-release (e.g. 2.0.0-dev1)..."
    get_prerelease_url
    log_info "Using pre-release asset: $PUBLISH_URL"
  else
    PUBLISH_URL="${RELEASE_BASE}/jacred-linux-${ARCH}.zip"
    log_info "Using release asset: jacred-linux-${ARCH}.zip"
  fi

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
