#!/usr/bin/env bash
# Build JacRed for linux-arm64, linux-amd64, and windows
# Output: dist/<platform>/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

OUTPUT_BASE="${OUTPUT_BASE:-$SCRIPT_DIR/dist}"

# Build to a temp dir so existing dist/ is never copied into a later publish output
BUILD_ROOT="$(mktemp -d)"
trap 'rm -rf "$BUILD_ROOT"' EXIT

PUBLISH_OPTS=(
  --configuration Release
  --self-contained true
  -p:PublishTrimmed=false
  -p:PublishSingleFile=true
  -p:DebugType=None
  -p:EnableCompressionInSingleFile=true
  -p:OptimizationPreference=Speed
  -p:SuppressTrimAnalysisWarnings=true
  -p:IlcOptimizationPreference=Speed
  -p:IlcFoldIdenticalMethodBodies=true
)

build_for() {
  local rid="$1"
  local out_dir="$2"
  local name="$3"
  echo "==> Building for $name (RID: $rid) -> $out_dir"
  dotnet publish . \
    --runtime "$rid" \
    --output "$out_dir" \
    "${PUBLISH_OPTS[@]}" \
    --verbosity minimal
  echo "    Done: $out_dir"
}

# Restore once for all targets
echo "==> Restoring packages..."
dotnet restore --verbosity minimal

# Linux ARM64
build_for "linux-arm64" "$BUILD_ROOT/linux-arm64" "linux-arm64"

# Linux AMD64 (x64)
build_for "linux-x64" "$BUILD_ROOT/linux-amd64" "linux-amd64"

# Windows x64
build_for "win-x64" "$BUILD_ROOT/windows-x64" "windows-x64"

# Replace dist with build result (avoids dist/ from project being copied into publish output)
echo "==> Writing to $OUTPUT_BASE ..."
OUTPUT_NEW="${OUTPUT_BASE}.new.$$"
mkdir -p "$OUTPUT_NEW"
mv "$BUILD_ROOT"/linux-arm64 "$BUILD_ROOT"/linux-amd64 "$BUILD_ROOT"/windows-x64 "$OUTPUT_NEW/"
rm -rf "$OUTPUT_BASE"
mv "$OUTPUT_NEW" "$OUTPUT_BASE"

echo ""
echo "Build complete. Outputs:"
echo "  $OUTPUT_BASE/linux-arm64/   (Linux ARM64)"
echo "  $OUTPUT_BASE/linux-amd64/   (Linux AMD64)"
echo "  $OUTPUT_BASE/windows-x64/   (Windows x64)"
