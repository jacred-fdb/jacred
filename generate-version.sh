#!/usr/bin/env bash
# Generate VersionInfo.g.cs from git information

set -euo pipefail

OUTPUT_FILE="$1"

GIT_SHA=$(git rev-parse --short=8 HEAD 2>/dev/null || echo "unknown")
GIT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")
BUILD_DATE=$(date -u +"%Y-%m-%d %H:%M:%S UTC")

VERSION=""

EXACT_TAG=$(git describe --tags --exact-match HEAD 2>/dev/null || echo "")
if [[ -n "$EXACT_TAG" ]]; then
    VERSION=$(echo "$EXACT_TAG" | sed 's/^v//')
else
    LATEST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")

    if [[ -n "$LATEST_TAG" ]]; then
        BASE_VERSION=$(echo "$LATEST_TAG" | sed 's/^v//')

        if [[ "$GIT_BRANCH" == "main" ]] || [[ "$GIT_BRANCH" == "master" ]]; then
            VERSION="${BASE_VERSION}-next+${GIT_SHA}"
        else
            VERSION="${BASE_VERSION}-next+${GIT_SHA}"
        fi
    else
        if [[ "$GIT_BRANCH" != "unknown" ]] && [[ "$GIT_BRANCH" != "HEAD" ]]; then
            SANITIZED_BRANCH=$(echo "$GIT_BRANCH" | sed 's/[^a-zA-Z0-9._-]/-/g')
            VERSION="dev-${SANITIZED_BRANCH}+${GIT_SHA}"
        else
            VERSION="dev+${GIT_SHA}"
        fi
    fi
fi

if [[ -z "$VERSION" ]]; then
    VERSION="unknown"
fi

cat > "$OUTPUT_FILE" <<EOF
namespace JacRed;

/// <summary>
/// Auto-generated version information from Git.
/// </summary>
internal static class VersionInfo
{
    /// <summary>
    /// Git commit SHA (short, 8 characters).
    /// </summary>
    public static string GitSha { get; } = "$GIT_SHA";

    /// <summary>
    /// Git branch name.
    /// </summary>
    public static string GitBranch { get; } = "$GIT_BRANCH";

    /// <summary>
    /// Build date and time (UTC).
    /// </summary>
    public static string BuildDate { get; } = "$BUILD_DATE";

    /// <summary>
    /// Full version string extracted from git tags or branch name.
    /// </summary>
    public static string Version { get; } = "$VERSION";
}
EOF
