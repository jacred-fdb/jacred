#!/usr/bin/env bash
# FlareSolverr VPS egress check (run on the host where FS listens on :8191).
# Expect without residential PROXY_*: status=error / timeout.
# After PROXY_URL on the container: status=ok, no "Just a moment", tracker HTML present.
set -euo pipefail

FS_URL="${FS_URL:-http://127.0.0.1:8191/v1}"
TEST_URL="${TEST_URL:-https://rutracker.org/forum/tracker.php?nm=}"
MAX_TIMEOUT_MS="${MAX_TIMEOUT_MS:-300000}"
CURL_MAX=$(( MAX_TIMEOUT_MS / 1000 + 30 ))

echo "POST $FS_URL  url=$TEST_URL  maxTimeout=$MAX_TIMEOUT_MS"
resp="$(curl -sS -m "$CURL_MAX" -X POST "$FS_URL" -H 'Content-Type: application/json' \
  -d "{\"cmd\":\"request.get\",\"url\":\"$TEST_URL\",\"maxTimeout\":$MAX_TIMEOUT_MS}")"

python3 -c '
import json, sys
r = json.loads(sys.argv[1])
b = (r.get("solution") or {}).get("response") or ""
print("status:", r.get("status"))
print("message:", r.get("message"))
print("http:", (r.get("solution") or {}).get("status"), "len:", len(b))
print("just_a_moment:", "Just a moment" in b or "Один момент" in b)
print("torTopic:", "torTopic" in b or "tt-" in b)
' "$resp"
