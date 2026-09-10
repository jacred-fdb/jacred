#!/usr/bin/env python3
"""Compare FlareSolverr vs plain HTTP for Kinozal Cloudflare.

JacRed already GETs browse.php through FlareSolverr. Magnets need
get_srv_details.php (info hash). FlareSolverr request.post is flaky in the
shared `jacred` session (often returns the previous browse tab). Prefer GET
of the same URL (id/action are query params).

Run on the host where FlareSolverr listens (default :8191). Session name
matches JacRed (`jacred`) so Chromium already has tracker cookies if the
crawler is running. Login/password is not needed when the browse flags show
logged_in: True. Do not put uid/pass in this file.

  python3 scripts/flaresolverr_kinozal_cf_check.py
  BROWSE_URL='https://kinozal.guru/browse.php?c=22&page=0' \\
    python3 scripts/flaresolverr_kinozal_cf_check.py

Paste the stdout of this script for investigation. Bodies are truncated;
set SAVE_DIR=/tmp/kz-cf-check to dump full HTML.
"""

from __future__ import annotations

import json
import os
import re
import ssl
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

FS_URL = os.environ.get("FS_URL", "http://127.0.0.1:8191/v1")
SESSION = os.environ.get("FS_SESSION", "jacred")
# page=0 of a live category — year-filtered ParseAllTask pages are often empty.
BROWSE_URL = os.environ.get(
    "BROWSE_URL",
    "https://kinozal.guru/browse.php?c=22&page=0",
)
TORRENT_ID = os.environ.get("TORRENT_ID", "").strip()
MAX_TIMEOUT_MS = int(os.environ.get("MAX_TIMEOUT_MS", "300000"))
CURL_MAX = MAX_TIMEOUT_MS / 1000 + 30
SAVE_DIR = os.environ.get("SAVE_DIR", "").strip()
SNIPPET = 400

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)


def cookies_from_env() -> list[dict[str, str]]:
    uid = os.environ.get("KINOZAL_UID", "").strip()
    password = os.environ.get("KINOZAL_PASS", "").strip()
    if not uid or not password:
        return []
    return [{"name": "uid", "value": uid}, {"name": "pass", "value": password}]


def save(name: str, body: str) -> None:
    if not SAVE_DIR:
        return
    path = Path(SAVE_DIR)
    path.mkdir(parents=True, exist_ok=True)
    (path / name).write_text(body, encoding="utf-8", errors="replace")


def snippet(body: str) -> str:
    return body[:SNIPPET].replace("\n", " ")


INFO_HASH = re.compile(r"Инфо хеш:\s*([A-Fa-f0-9]{40})")
# href="/details.php?id=" — do not match userdetails.php?id= (logged-in profile).
TORRENT_HREF = re.compile(r"""href=["']/?details\.php\?id=(\d+)["']""", re.I)


def flags(body: str) -> dict[str, Any]:
    info = INFO_HASH.search(body)
    return {
        "len": len(body),
        "cf_just_a_moment": "Just a moment" in body or "Один момент" in body,
        "cf_challenge": "challenge-platform" in body or "cf-browser-verification" in body,
        "cf_chl_opt": "_cf_chl_opt" in body,
        "kinozal_rows": "first bg" in body or "class=bg" in body,
        "details.php": bool(TORRENT_HREF.search(body)),
        "logged_in": ">Выход</a>" in body,
        "login_wall": "take_login" in body or "login.php" in body,
        "not_found": "Торрент файл не найден" in body,
        "nginx_503": "503 Service Temporarily Unavailable" in body,
        "info_hash_label": bool(info),
        "hash40": info.group(1) if info else None,
        "rutracker_html": "rutracker.org" in body.lower() or "RuTracker.org" in body,
        "stale_browse": "Раздачи :: Кинозал" in body and not bool(info),
        "title": (m.group(1) if (m := re.search(r"<title>([^<]+)</title>", body, re.I)) else ""),
    }


def print_flags(label: str, data: dict[str, Any]) -> None:
    print(f"  {label}:")
    for key, value in data.items():
        print(f"    {key}: {value}")


def fs_call(cmd: str, url: str, post_data: str | None = None) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "cmd": cmd,
        "session": SESSION,
        "url": url,
        "maxTimeout": MAX_TIMEOUT_MS,
    }
    jar = cookies_from_env()
    if jar:
        payload["cookies"] = jar
    if post_data is not None:
        payload["postData"] = post_data

    raw = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        FS_URL,
        data=raw,
        headers={"Content-Type": "application/json", "Accept": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=CURL_MAX, context=CTX) as resp:
            return json.loads(resp.read().decode("utf-8", errors="replace"))
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        return {"status": "error", "message": f"HTTP {exc.code}", "raw": body[:SNIPPET]}
    except Exception as exc:
        return {"status": "error", "message": f"{type(exc).__name__}: {exc}"}


def fs_solution(root: dict[str, Any]) -> tuple[int | None, str]:
    solution = root.get("solution") or {}
    return solution.get("status"), solution.get("response") or ""


def direct(url: str, data: bytes | None = None) -> tuple[int | None, dict[str, str], str]:
    headers = {
        "User-Agent": UA,
        "Accept": "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8",
    }
    if data is not None:
        headers["Content-Type"] = "application/x-www-form-urlencoded"
    req = urllib.request.Request(url, data=data, headers=headers, method="POST" if data else "GET")
    try:
        with urllib.request.urlopen(req, timeout=20, context=CTX) as resp:
            body = resp.read().decode("utf-8", errors="replace")
            hdrs = {k.lower(): v for k, v in resp.headers.items()}
            return resp.status, hdrs, body
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        hdrs = {k.lower(): v for k, v in exc.headers.items()} if exc.headers else {}
        return exc.code, hdrs, body
    except Exception as exc:
        return None, {}, f"{type(exc).__name__}: {exc}"


def interesting_headers(hdrs: dict[str, str]) -> dict[str, str]:
    keep = (
        "server",
        "cf-ray",
        "cf-mitigated",
        "cf-cache-status",
        "content-type",
        "location",
    )
    return {k: hdrs[k] for k in keep if k in hdrs}


def extract_ids(html: str) -> list[str]:
    seen: list[str] = []
    for torrent_id in TORRENT_HREF.findall(html):
        if torrent_id not in seen:
            seen.append(torrent_id)
    return seen


def main() -> int:
    cookies = cookies_from_env()
    print("== kinozal Cloudflare check ==")
    print(f"FS_URL: {FS_URL}")
    print(f"session: {SESSION}")
    print(f"browse: {BROWSE_URL}")
    print(f"cookies_from_env: {'uid+pass (values hidden)' if cookies else 'none (reuse Chromium session)'}")
    if SAVE_DIR:
        print(f"SAVE_DIR: {SAVE_DIR}")
    print()

    print("== 1 FlareSolverr GET browse.php ==")
    browse_fs = fs_call("request.get", BROWSE_URL)
    http, html = fs_solution(browse_fs)
    print(f"  status: {browse_fs.get('status')}")
    print(f"  message: {browse_fs.get('message')}")
    print(f"  http: {http}")
    print_flags("body", flags(html))
    ids = extract_ids(html)
    print(f"  ids_count: {len(ids)}")
    print(f"  ids: {','.join(ids[:12]) if ids else '(none)'}")
    print(f"  snippet: {snippet(html)}")
    save("1-browse-flaresolverr.html", html)
    print()
    if flags(html)["nginx_503"]:
        print("== stop: FlareSolverr got origin nginx 503 (transient). Re-run in a minute. ==")
    elif not ids:
        print("== stop: listing has no torrent href=/details.php?id= ==")
        print("  This page is empty or still year-filtered. Re-run with:")
        print("  BROWSE_URL='https://kinozal.guru/browse.php?c=22&page=0' python3 scripts/flaresolverr_kinozal_cf_check.py")
        print("  Login/password is not required when logged_in is True.")
    print("== 2 direct GET browse.php (expect CF challenge) ==")
    code, hdrs, body = direct(BROWSE_URL)
    print(f"  http: {code}")
    print(f"  headers: {interesting_headers(hdrs)}")
    print_flags("body", flags(body))
    print(f"  snippet: {snippet(body)}")
    save("2-browse-direct.html", body)
    print()

    if TORRENT_ID:
        torrent_id = TORRENT_ID
        print(f"  TORRENT_ID override: {torrent_id}")
    elif ids:
        torrent_id = ids[0]
    else:
        print("== stop: no torrent id (empty listing, not a missing login) ==")
        return 1

    hash_url = f"https://kinozal.guru/get_srv_details.php?id={torrent_id}&action=2"
    post_data = f"id={torrent_id}&action=2"
    print(f"== using torrent id {torrent_id} ==")
    print(f"  hash_url: {hash_url}")
    print()

    print("== 3 direct POST get_srv_details.php (expect CF challenge) ==")
    code, hdrs, body = direct(hash_url, data=post_data.encode("utf-8"))
    print(f"  http: {code}")
    print(f"  headers: {interesting_headers(hdrs)}")
    print_flags("body", flags(body))
    print(f"  snippet: {snippet(body)}")
    save("3-hash-direct.html", body)
    print()

    print("== 4 FlareSolverr POST get_srv_details.php (often stale tab) ==")
    hash_fs = fs_call("request.post", hash_url, post_data=post_data)
    http, html = fs_solution(hash_fs)
    print(f"  status: {hash_fs.get('status')}")
    print(f"  message: {hash_fs.get('message')}")
    print(f"  http: {http}")
    print_flags("body", flags(html))
    print(f"  snippet: {snippet(html)}")
    save("4-hash-flaresolverr-post.html", html)
    print()

    print("== 5 FlareSolverr GET get_srv_details.php (JacRed path) ==")
    hash_get = fs_call("request.get", hash_url)
    get_http, get_html = fs_solution(hash_get)
    print(f"  status: {hash_get.get('status')}")
    print(f"  message: {hash_get.get('message')}")
    print(f"  http: {get_http}")
    print_flags("body", flags(get_html))
    print(f"  snippet: {snippet(get_html)}")
    save("5-hash-flaresolverr-get.html", get_html)
    print()

    print("== verdict ==")
    browse_flags = flags(fs_solution(browse_fs)[1])
    post_flags = flags(html)
    get_flags = flags(get_html)
    browse_ok = browse_flags["kinozal_rows"] and bool(ids) and not browse_flags["cf_just_a_moment"]
    direct_blocked = (code in (403, 503)) or flags(body)["cf_just_a_moment"]
    post_ok = bool(post_flags["hash40"]) and not post_flags["rutracker_html"] and not post_flags["not_found"]
    get_ok = bool(get_flags["hash40"]) and not get_flags["rutracker_html"] and not get_flags["not_found"]
    print(f"  browse_via_flaresolverr: {'OK' if browse_ok else 'FAIL'}")
    print(f"  hash_direct_blocked: {'YES' if direct_blocked else 'NO / unexpected'}")
    print(f"  hash_via_flaresolverr_post: {'OK' if post_ok else 'FAIL'}")
    print(f"  hash_via_flaresolverr_get: {'OK' if get_ok else 'FAIL'}")
    if post_flags["stale_browse"] or get_flags["stale_browse"]:
        print("  note: hash call returned a browse listing (shared jacred session / POST did not navigate).")
    if post_flags["rutracker_html"] or get_flags["rutracker_html"]:
        print("  note: FlareSolverr returned rutracker HTML (shared jacred session). Re-run when parse is idle.")
    if browse_ok and get_ok and direct_blocked:
        print("  next: JacRed should GET get_srv_details.php through FlareSolverr (not POST).")
    elif browse_ok and post_ok and not get_ok:
        print("  next: POST worked, GET did not — paste this stdout.")
    elif browse_ok and not get_ok:
        print("  next: listing works; FlareSolverr hash GET failed — paste this stdout.")
    else:
        print("  next: paste this stdout; listing or FS session needs another look.")
    return 0 if browse_ok else 1


if __name__ == "__main__":
    sys.exit(main())
