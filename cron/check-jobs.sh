#!/usr/bin/env bash
# Wrapper for check-jobs.py (status table + optional --fix-stuck).
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "${SCRIPT_DIR}/check-jobs.py" "$@"
