#!/usr/bin/env bash
# Build JacRed for current architecture by default, or all architectures with --all flag
# Output: dist/<platform>/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

OUTPUT_BASE="${OUTPUT_BASE:-$SCRIPT_DIR/dist}"

# Check for --all flag
BUILD_ALL=false
if [[ "${1:-}" == "--all" ]]; then
  BUILD_ALL=true
fi

# Detect current OS and architecture
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
  
  echo "${os}-${arch}"
}

# Build to a temp dir in project root so existing dist/ is never copied into a later publish output
BUILD_ROOT="$SCRIPT_DIR/.builds"
rm -rf "$BUILD_ROOT"
mkdir -p "$BUILD_ROOT"
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
  dotnet publish JacRed.csproj \
    --runtime "$rid" \
    --output "$out_dir" \
    "${PUBLISH_OPTS[@]}" \
    --verbosity minimal
  echo "    Done: $out_dir"
}

# Restore once for all targets
echo "==> Restoring packages..."
dotnet restore --verbosity minimal

ALL_PLATFORMS=(
  linux-arm64
  linux-amd64
  windows-x64
  osx-arm64
  osx-amd64
)

if [[ "$BUILD_ALL" == "true" ]]; then
  PLATFORMS=("${ALL_PLATFORMS[@]}")
  echo "==> Building for all platforms..."
else
  CURRENT_PLATFORM=$(detect_current_platform)
  PLATFORMS=("$CURRENT_PLATFORM")
  echo "==> Building for current platform: $CURRENT_PLATFORM"
fi

for platform in "${PLATFORMS[@]}"; do
  build_for "$platform" "$BUILD_ROOT/$platform" "$platform"
done

# Merge build result with existing dist, preserving test data files
# This avoids copying large fdb files that are excluded from .csproj
echo "==> Writing to $OUTPUT_BASE ..."

for platform in "${PLATFORMS[@]}"; do
  target_dir="$OUTPUT_BASE/$platform"
  build_dir="$BUILD_ROOT/$platform"
  
  # Create target directory if it doesn't exist
  mkdir -p "$target_dir"
  
  # Temporarily move test data files out of the way
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
  
  # Remove old build files (but keep Data directory structure)
  if [[ -d "$target_dir/Data" ]]; then
    find "$target_dir/Data" -mindepth 1 -maxdepth 1 -type d ! -name "fdb" -exec rm -rf {} + 2>/dev/null || true
    find "$target_dir/Data" -mindepth 1 -maxdepth 1 -type f ! -name "masterDb.bz" -exec rm -f {} + 2>/dev/null || true
  fi
  find "$target_dir" -mindepth 1 -maxdepth 1 ! -name "Data" -exec rm -rf {} + 2>/dev/null || true
  
  # Copy new build output (fdb and masterDb.bz are excluded by .csproj, so won't be copied)
  # Exclude dist directory if it exists in build output to prevent nesting
  (cd "$build_dir" && find . -mindepth 1 -maxdepth 1 ! -name dist -exec cp -r {} "$target_dir/" \; 2>/dev/null || true)
  
  # Restore preserved test data files
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
