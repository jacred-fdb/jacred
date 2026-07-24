#!/usr/bin/env bash
# Build Vue SPA (web/) into a fresh wwwroot/ for .NET publish.
# wwwroot is fully generated — not tracked in git (see .gitignore).
# Runtime may later write wwwroot/trackers.txt (TrackersCron).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WEB="$ROOT/web"
WWW="$ROOT/wwwroot"
DIST="$WEB/dist"
OPENAPI_SRC="$WEB/public/openapi.yaml"

if [[ ! -d "$WEB" ]]; then
  echo "error: web/ not found at $WEB" >&2
  exit 1
fi

if [[ ! -f "$OPENAPI_SRC" ]]; then
  echo "error: $OPENAPI_SRC missing — API contract must live in web/public" >&2
  exit 1
fi

echo "==> Building web UI..."
cd "$WEB"
if [[ -f package-lock.json ]]; then
  npm ci
else
  npm install
fi
npm run build

if [[ ! -f "$DIST/index.html" ]]; then
  echo "error: web/dist/index.html missing after build" >&2
  exit 1
fi
if [[ ! -f "$DIST/openapi.yaml" ]]; then
  echo "error: web/dist/openapi.yaml missing — expected copy from public/" >&2
  exit 1
fi
if [[ ! -f "$DIST/sw.js" ]]; then
  echo "error: web/dist/sw.js missing — PWA service worker was not generated" >&2
  exit 1
fi

# Preserve runtime trackers.txt across rebuilds when present
TRACKERS_BAK=""
if [[ -f "$WWW/trackers.txt" ]]; then
  TRACKERS_BAK="$(mktemp)"
  cp "$WWW/trackers.txt" "$TRACKERS_BAK"
fi

echo "==> Recreating wwwroot from web/dist..."
rm -rf "$WWW"
mkdir -p "$WWW"
cp -a "$DIST"/. "$WWW"/

if [[ -n "$TRACKERS_BAK" ]]; then
  cp "$TRACKERS_BAK" "$WWW/trackers.txt"
  rm -f "$TRACKERS_BAK"
fi

if [[ ! -f "$WWW/index.html" || ! -f "$WWW/openapi.yaml" || ! -f "$WWW/sw.js" ]]; then
  echo "error: wwwroot incomplete after merge" >&2
  exit 1
fi

ASSET_COUNT="$(find "$WWW/assets" -type f | wc -l | tr -d ' ')"
echo "==> wwwroot ready ($ASSET_COUNT files under assets/)"
