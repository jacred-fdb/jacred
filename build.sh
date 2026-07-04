#!/usr/bin/env bash
# Build JacRed for current platform (default), a specific RID, or all platforms.
# Output: dist/<platform>/
#
# Usage:
#   ./build.sh                    # current OS/arch
#   ./build.sh linux-arm64        # single target
#   ./build.sh linux-x64 linux-arm64
#   ./build.sh --all              # all supported targets
#   ./build.sh --help

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

OUTPUT_BASE="${OUTPUT_BASE:-$SCRIPT_DIR/dist}"

# https://learn.microsoft.com/en-us/dotnet/core/rid-catalog
ALL_PLATFORMS=(
  linux-x64
  linux-musl-x64
  linux-musl-arm64
  linux-arm
  linux-arm64
  win-x64
  win-x86
  win-arm64
  osx-arm64
  osx-amd64
)

usage() {
  cat <<EOF
Usage: $(basename "$0") [OPTIONS] [TARGET...]

Build JacRed into dist/<target>/.

Options:
  --all       Build all supported targets
  -h, --help  Show this help

Targets (RID):
  $(printf '  %s\n' "${ALL_PLATFORMS[@]}")

Aliases:
  linux-amd64, amd64     -> linux-x64
  linux-aarch64, arm64   -> linux-arm64 (when prefixed with linux- or alone)
  osx-x64                -> osx-amd64
  windows-x64            -> win-x64

Examples:
  $(basename "$0")
  $(basename "$0") linux-arm64
  $(basename "$0") linux-x64 linux-arm64
  $(basename "$0") --all
EOF
}

detect_current_platform() {
  local os=""
  local arch=""

  case "$(uname -s)" in
    Linux*)   os="linux" ;;
    Darwin*)  os="osx" ;;
    MINGW*|MSYS*|CYGWIN*) os="windows" ;;
    *)        echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
  esac

  case "$(uname -m)" in
    x86_64|amd64) arch="amd64" ;;
    arm64|aarch64) arch="arm64" ;;
    *)        echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
  esac

  if [[ "$os" == "windows" ]]; then
    echo "win-${arch}"
  elif [[ "$os" == "linux" && "$arch" == "amd64" ]]; then
    echo "linux-x64"
  else
    echo "${os}-${arch}"
  fi
}

normalize_platform() {
  local input
  input="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
  case "$input" in
    linux-amd64|amd64) echo "linux-x64" ;;
    linux-aarch64) echo "linux-arm64" ;;
    arm64) echo "linux-arm64" ;;
    osx-x64|osx-amd64) echo "osx-amd64" ;;
    windows-x64|win-amd64) echo "win-x64" ;;
    windows-x86|win-x86) echo "win-x86" ;;
    windows-arm64|win-arm) echo "win-arm64" ;;
    *) echo "$input" ;;
  esac
}

is_known_platform() {
  local platform="$1"
  local known
  for known in "${ALL_PLATFORMS[@]}"; do
    [[ "$known" == "$platform" ]] && return 0
  done
  return 1
}

BUILD_ALL=false
REQUESTED=()

for arg in "$@"; do
  case "$arg" in
    --all) BUILD_ALL=true ;;
    -h|--help) usage; exit 0 ;;
    -*) echo "Unknown option: $arg" >&2; usage >&2; exit 1 ;;
    *) REQUESTED+=("$(normalize_platform "$arg")") ;;
  esac
done

PLATFORMS=()

if [[ "$BUILD_ALL" == "true" ]]; then
  if [[ ${#REQUESTED[@]} -gt 0 ]]; then
    echo "Cannot combine --all with explicit targets." >&2
    exit 1
  fi
  PLATFORMS=("${ALL_PLATFORMS[@]}")
  echo "==> Building for all platforms..."
elif [[ ${#REQUESTED[@]} -gt 0 ]]; then
  for platform in "${REQUESTED[@]}"; do
    if ! is_known_platform "$platform"; then
      echo "Unknown target: $platform" >&2
      echo "Run $(basename "$0") --help for supported targets." >&2
      exit 1
    fi
    PLATFORMS+=("$platform")
  done
  echo "==> Building for: ${PLATFORMS[*]}"
else
  CURRENT_PLATFORM="$(detect_current_platform)"
  if ! is_known_platform "$CURRENT_PLATFORM"; then
    echo "Current platform not in build list: $CURRENT_PLATFORM" >&2
    exit 1
  fi
  PLATFORMS=("$CURRENT_PLATFORM")
  echo "==> Building for current platform: $CURRENT_PLATFORM"
fi

BUILD_ROOT="$SCRIPT_DIR/.builds"
rm -rf "$BUILD_ROOT"
mkdir -p "$BUILD_ROOT"
trap 'rm -rf "$BUILD_ROOT"' EXIT

PUBLISH_OPTS=(
  --configuration Release
  --self-contained true
  -p:PublishTrimmed=false
  -p:DebugType=None
  -p:OptimizationPreference=Speed
  -p:SuppressTrimAnalysisWarnings=true
  -p:IlcOptimizationPreference=Speed
  -p:IlcFoldIdenticalMethodBodies=true
)

# dotnet/runtime#123324 — EnableCompressionInSingleFile on osx-arm64 corrupts the heap
# (random AccessViolationException in Kestrel routing, FrozenDictionary, etc.).
# Deploy macOS as a standard self-contained directory instead of a bundled executable.
PUBLISH_OPTS_FOR=()

publish_opts_for() {
  local rid="$1"
  PUBLISH_OPTS_FOR=("${PUBLISH_OPTS[@]}")
  case "$rid" in
    osx-*)
      PUBLISH_OPTS_FOR+=(-p:PublishSingleFile=false)
      ;;
    *)
      PUBLISH_OPTS_FOR+=(-p:PublishSingleFile=true)
      PUBLISH_OPTS_FOR+=(-p:EnableCompressionInSingleFile=true)
      ;;
  esac
}

build_for() {
  local rid="$1"
  local out_dir="$2"
  local name="$3"
  publish_opts_for "$rid"
  echo "==> Building for $name (RID: $rid) -> $out_dir"
  dotnet publish JacRed.csproj \
    --runtime "$rid" \
    --output "$out_dir" \
    "${PUBLISH_OPTS_FOR[@]}" \
    --verbosity minimal
  echo "    Done: $out_dir"
}

echo "==> Restoring packages..."
dotnet restore --verbosity minimal

bump_sw_cache_version() {
  local sw_file="$SCRIPT_DIR/wwwroot/sw.js"
  [[ -f "$sw_file" ]] || return 0
  local sw_tag
  sw_tag="$(git describe --tags --always --dirty 2>/dev/null | sed 's/^v//' | tr '/ ' '-')"
  sw_tag="${sw_tag:-dev}"
  perl -pi -e "s/const CACHE_NAME = 'jacred-static-[^']+'/const CACHE_NAME = 'jacred-static-${sw_tag}'/" "$sw_file"
  echo "==> Service worker cache: jacred-static-${sw_tag}"
}

bump_sw_cache_version

for platform in "${PLATFORMS[@]}"; do
  build_for "$platform" "$BUILD_ROOT/$platform" "$platform"
done

echo "==> Writing to $OUTPUT_BASE ..."

for platform in "${PLATFORMS[@]}"; do
  target_dir="$OUTPUT_BASE/$platform"
  build_dir="$BUILD_ROOT/$platform"

  mkdir -p "$target_dir"

  temp_preserve="$(mktemp -d)"

  if [[ -d "$target_dir/Data/fdb" ]]; then
    mv "$target_dir/Data/fdb" "$temp_preserve/fdb" 2>/dev/null || true
  fi

  if [[ -f "$target_dir/Data/masterDb.bz" ]]; then
    mv "$target_dir/Data/masterDb.bz" "$temp_preserve/masterDb.bz" 2>/dev/null || true
  fi

  if [[ -f "$target_dir/init.yaml" ]]; then
    mv "$target_dir/init.yaml" "$temp_preserve/init.yaml" 2>/dev/null || true
  fi

  if [[ -d "$target_dir/Data" ]]; then
    find "$target_dir/Data" -mindepth 1 -maxdepth 1 -type d ! -name "fdb" -exec rm -rf {} + 2>/dev/null || true
    find "$target_dir/Data" -mindepth 1 -maxdepth 1 -type f ! -name "masterDb.bz" -exec rm -f {} + 2>/dev/null || true
  fi
  find "$target_dir" -mindepth 1 -maxdepth 1 ! -name "Data" -exec rm -rf {} + 2>/dev/null || true

  (cd "$build_dir" && find . -mindepth 1 -maxdepth 1 ! -name dist -exec cp -r {} "$target_dir/" \; 2>/dev/null || true)

  if [[ -d "$temp_preserve/fdb" ]]; then
    mkdir -p "$target_dir/Data"
    mv "$temp_preserve/fdb" "$target_dir/Data/fdb" 2>/dev/null || true
  fi

  if [[ -f "$temp_preserve/masterDb.bz" ]]; then
    mkdir -p "$target_dir/Data"
    mv "$temp_preserve/masterDb.bz" "$target_dir/Data/masterDb.bz" 2>/dev/null || true
  fi

  if [[ -f "$temp_preserve/init.yaml" ]]; then
    mv "$temp_preserve/init.yaml" "$target_dir/init.yaml" 2>/dev/null || true
  fi

  rm -rf "$temp_preserve"
done

echo ""
echo "Build complete. Outputs:"
for platform in "${PLATFORMS[@]}"; do
  echo "  $OUTPUT_BASE/$platform/   ($platform)"
done
