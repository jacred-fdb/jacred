#!/usr/bin/env bash
# Bump service worker CACHE_NAME so clients invalidate static assets after deploy.
#
# Usage:
#   ./scripts/bump-sw-cache.sh              # from git describe
#   ./scripts/bump-sw-cache.sh 2.6.0        # explicit tag/version

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SW_FILE="${SW_FILE:-$REPO_ROOT/wwwroot/sw.js}"

[[ -f "$SW_FILE" ]] || exit 0

sw_tag="${1:-${SW_CACHE_TAG:-}}"
if [[ -z "$sw_tag" ]]; then
  sw_tag="$(git -C "$REPO_ROOT" describe --tags --always --dirty 2>/dev/null | sed 's/^v//' | tr '/ ' '-')"
fi
sw_tag="${sw_tag:-dev}"

tmp="$(mktemp)"
sed "s/const CACHE_NAME = 'jacred-static-[^']*'/const CACHE_NAME = 'jacred-static-${sw_tag}'/" "$SW_FILE" > "$tmp"
mv "$tmp" "$SW_FILE"
echo "Service worker cache: jacred-static-${sw_tag}"
