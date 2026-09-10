#!/usr/bin/env python3
"""Live spike: FlareSolverr cookies + curl_cffi Chrome TLS vs page.goto.

JacRed today throws away solution.cookies/userAgent and navigates Chromium
for every guarded GET. JacBlack cffetch (curl_cffi) reused the jar at 0.13s
vs 3.9s — except kinozal on 2026-09-09 rejected every impersonate profile.

This script does not talk to JacRed. Cookie values are never printed.

  python3 scripts/cf_fastpath_impersonate_check.py
  ssh master.jacred.stream 'python3 -' < scripts/cf_fastpath_impersonate_check.py

  PROXY=socks5://127.0.0.1:20001 REPEAT=5 \\
    python3 scripts/cf_fastpath_impersonate_check.py

Needs: live FlareSolverr, curl_cffi (pip install 'curl_cffi>=0.7,<1.0').
"""

from __future__ import annotations

import json
import os
import re
import statistics
import ssl
import sys
import time
import urllib.error
import urllib.request
from typing import Any

FS_URL = os.environ.get("FS_URL", "http://127.0.0.1:8191/v1")
KZ_SESSION = os.environ.get("FS_SESSION", "jacred-kinozal_guru")
RT_SESSION = os.environ.get("RT_SESSION", "jacred-rutracker_org")
PROXY = os.environ.get("PROXY", os.environ.get("PROXY_URL", "")).strip()
REPEAT = max(1, int(os.environ.get("REPEAT", "5")))
REPEAT_AFTER_SEC = float(os.environ.get("REPEAT_AFTER_SEC", "0"))
MAX_TIMEOUT_MS = int(os.environ.get("MAX_TIMEOUT_MS", "180000"))
CURL_MAX = MAX_TIMEOUT_MS / 1000 + 30
HOST = os.environ.get("KINOZAL_HOST", "https://kinozal.guru").rstrip("/")
BROWSE_URL = os.environ.get("BROWSE_URL", f"{HOST}/browse.php?c=22&page=0")
EMPTY_URL = os.environ.get(
    "EMPTY_URL",
    f"{HOST}/browse.php?c=45&page=15&d=2026&t=1",
)
RUTRACKER_URL = os.environ.get(
    "RUTRACKER_URL",
    "https://rutracker.org/forum/viewforum.php?f=2090",
)
IMPERSONATE = os.environ.get("IMPERSONATE", "").strip()
PROFILES = [
    p
    for p in os.environ.get(
        "PROFILES",
        "chrome131,chrome124,chrome136,chrome120",
    ).split(",")
    if p.strip()
]

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

INFO_HASH = re.compile(r"Инфо хеш:\s*([A-Fa-f0-9]{40})")
TORRENT_HREF = re.compile(r"""href=["']/details\.php\?id=(\d+)["']""", re.I)
META_CHARSET = re.compile(rb'charset=["\']?\s*([\w-]+)', re.I)
CHROME_MAJOR = re.compile(r"Chrome/(\d+)", re.I)


def cookies_from_env() -> list[dict[str, str]]:
    uid = os.environ.get("KINOZAL_UID", "").strip()
    password = os.environ.get("KINOZAL_PASS", "").strip()
    if not uid or not password:
        return []
    return [{"name": "uid", "value": uid}, {"name": "pass", "value": password}]


def cookie_header(jar: list[dict[str, Any]]) -> str:
    parts: list[str] = []
    seen: set[str] = set()
    for item in jar:
        name = (item.get("name") or "").strip()
        if not name or name in seen:
            continue
        seen.add(name)
        parts.append(f"{name}={item.get('value') or ''}")
    return "; ".join(parts)


def cookie_names(header: str) -> str:
    names = []
    for part in header.split(";"):
        eq = part.find("=")
        if eq > 0:
            names.append(part[:eq].strip())
    return ",".join(names) if names else "(none)"


def flags(body: str) -> dict[str, Any]:
    html = body or ""
    info = INFO_HASH.search(html)
    low = html.lower()
    return {
        "len": len(html),
        "cf_just_a_moment": "just a moment" in low or "Один момент" in html,
        "cf_chl_opt": "_cf_chl_opt" in html,
        "challenge_platform": "challenge-platform" in low,
        "t_peer": "t_peer" in html,
        "empty_search": "Нет активных раздач" in html,
        "logged_in": ">Выход</a>" in html,
        "details": bool(TORRENT_HREF.search(html)),
        "hash40": bool(info),
        "torTopic": "tortopic" in low or 'class="tt-"' in low or "viewtopic.php?t=" in low,
        "rutracker": "rutracker.org" in low,
        "nginx_503": "503 service temporarily unavailable" in low,
        "title": (m.group(1)[:80] if (m := re.search(r"<title>([^<]+)</title>", html, re.I)) else ""),
    }


def gate(body: str, kind: str) -> str:
    f = flags(body)
    if f["cf_just_a_moment"] or f["cf_chl_opt"]:
        return "challenge"
    if f["nginx_503"]:
        return "origin_503"
    if kind == "browse":
        if f["t_peer"] and f["details"]:
            return "listing"
        if f["empty_search"]:
            return "empty_search"
        return "stale_or_other"
    if kind == "hash":
        return "hash" if f["hash40"] else "no_hash"
    if kind == "forum":
        return "forum" if f["torTopic"] or "viewforum" in (f["title"] or "").lower() else "not_forum"
    return "?"


def fs_call(session: str, url: str) -> tuple[dict[str, Any], float]:
    payload: dict[str, Any] = {
        "cmd": "request.get",
        "session": session,
        "url": url,
        "maxTimeout": MAX_TIMEOUT_MS,
    }
    extra = cookies_from_env()
    if extra:
        payload["cookies"] = extra
    raw = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        FS_URL,
        data=raw,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=CURL_MAX, context=CTX) as resp:
            root = json.loads(resp.read().decode("utf-8", errors="replace"))
    except Exception as exc:
        return {"status": "error", "message": f"{type(exc).__name__}: {exc}"}, time.time() - t0
    return root, time.time() - t0


def solution_of(root: dict[str, Any]) -> dict[str, Any]:
    return root.get("solution") or {}


def decode_bytes(content: bytes, declared: str | None) -> str:
    enc = (declared or "").lower()
    if not enc or enc in ("iso-8859-1", "ascii"):
        found = META_CHARSET.search(content[:4096])
        enc = found.group(1).decode("ascii", "ignore") if found else "utf-8"
    try:
        return content.decode(enc, errors="replace")
    except LookupError:
        return content.decode("utf-8", errors="replace")


def plain_get(url: str, cookie: str, user_agent: str) -> tuple[int, bool, str, float]:
    headers = {
        "User-Agent": user_agent or "Mozilla/5.0",
        "Accept": "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8",
    }
    if cookie:
        headers["Cookie"] = cookie
    req = urllib.request.Request(url, headers=headers, method="GET")
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=25, context=CTX) as resp:
            body = decode_bytes(resp.read(), resp.headers.get_content_charset())
            hdrs = {k.lower() for k in resp.headers.keys()}
            return int(resp.status), "cf-mitigated" in hdrs, body, time.time() - t0
    except urllib.error.HTTPError as exc:
        body = decode_bytes(exc.read() or b"", None)
        hdrs = {k.lower() for k in (exc.headers.keys() if exc.headers else [])}
        return int(exc.code), "cf-mitigated" in hdrs, body, time.time() - t0
    except Exception as exc:
        return 0, False, f"{type(exc).__name__}: {exc}", time.time() - t0


def cffi_get(
    url: str,
    cookie: str,
    user_agent: str,
    impersonate: str,
    proxy: str,
) -> tuple[int, bool, str, float, str]:
    try:
        from curl_cffi import requests as cffi
    except ImportError:
        return 0, False, "", 0.0, "curl_cffi not installed"

    headers: dict[str, str] = {}
    if cookie:
        headers["Cookie"] = cookie
    if user_agent:
        headers["User-Agent"] = user_agent
    kwargs: dict[str, Any] = {
        "headers": headers,
        "impersonate": impersonate,
        "timeout": 25,
        "allow_redirects": True,
    }
    if proxy:
        kwargs["proxy"] = proxy
    t0 = time.time()
    try:
        response = cffi.get(url, **kwargs)
    except Exception as exc:
        return 0, False, f"{type(exc).__name__}: {exc}", time.time() - t0, ""
    mitigated = "cf-mitigated" in {k.lower() for k in response.headers.keys()}
    body = decode_bytes(response.content, response.encoding)
    return int(response.status_code), mitigated, body, time.time() - t0, ""


def pick_impersonate(user_agent: str) -> str:
    if IMPERSONATE:
        return IMPERSONATE
    m = CHROME_MAJOR.search(user_agent or "")
    if not m:
        return "chrome131"
    major = int(m.group(1))
    for name in ("chrome136", "chrome131", "chrome124", "chrome120"):
        n = int(re.search(r"\d+", name).group(0))
        if n <= major:
            return name
    return "chrome131"


def pct(values: list[float], p: float) -> float:
    if not values:
        return 0.0
    if len(values) == 1:
        return values[0]
    try:
        return float(statistics.quantiles(values, n=100, method="inclusive")[int(p) - 1])
    except Exception:
        ordered = sorted(values)
        idx = min(len(ordered) - 1, max(0, round((p / 100) * (len(ordered) - 1))))
        return ordered[idx]


def print_row(
    label: str,
    http: int,
    dt: float,
    mitigated: bool,
    body: str,
    kind: str,
    extra: str = "",
) -> None:
    f = flags(body if not body.startswith("ImportError") and "not installed" not in body else "")
    g = gate(body, kind) if http else "error"
    print(
        f"  {label:28} {dt:6.3f}s http={http:<4} cf-mitigated={str(mitigated):5} "
        f"gate={g:<14} len={f['len']:<6} {extra}".rstrip()
    )
    if f["title"]:
        print(f"    title: {f['title']}")


def summarize_times(label: str, times: list[float]) -> None:
    if not times:
        return
    print(
        f"  {label:28} n={len(times)} p50={pct(times, 50):.3f}s "
        f"p95={pct(times, 95):.3f}s min={min(times):.3f}s max={max(times):.3f}s"
    )


def run_url(
    label: str,
    url: str,
    kind: str,
    session: str,
    cookie: str,
    user_agent: str,
    impersonate: str,
    proxy: str,
) -> dict[str, Any]:
    print(f"\n== {label} ==")
    print(f"  url: {url}")

    root, fs_dt = fs_call(session, url)
    sol = solution_of(root)
    fs_html = sol.get("response") or ""
    fs_http = int(sol.get("status") or 0)
    ua_full = sol.get("userAgent") or user_agent or ""
    chrome = (CHROME_MAJOR.search(ua_full).group(1) if CHROME_MAJOR.search(ua_full) else "?")
    print(
        f"  FS status={root.get('status')} message={root.get('message')!r} "
        f"ua_chrome={chrome}"
    )
    print_row("FS request.get", fs_http, fs_dt, False, fs_html, kind)

    jar = sol.get("cookies") or []
    merged = cookie_header(jar)
    if cookie:
        # Keep previously captured clearance if this response omitted it.
        names = {p.split("=", 1)[0] for p in cookie.split("; ") if "=" in p}
        if "cf_clearance" in names and "cf_clearance" not in cookie_names(merged):
            merged = cookie
        else:
            # merge: new wins
            old = {p.split("=", 1)[0]: p.split("=", 1)[1] for p in cookie.split("; ") if "=" in p}
            new = {p.split("=", 1)[0]: p.split("=", 1)[1] for p in merged.split("; ") if "=" in p}
            old.update(new)
            merged = "; ".join(f"{k}={v}" for k, v in old.items())
    ua = ua_full or user_agent
    if not IMPERSONATE:
        impersonate = pick_impersonate(ua)
    print(f"  cookie_names: {cookie_names(merged)}")

    plain_http, plain_mit, plain_body, plain_dt = plain_get(url, merged, ua)
    print_row("plain urllib+cookie+UA", plain_http, plain_dt, plain_mit, plain_body, kind)

    cffi_times: list[float] = []
    last_cffi: tuple[int, bool, str] | None = None
    err = ""
    for i in range(REPEAT):
        http, mit, body, dt, err = cffi_get(url, merged, ua, impersonate, proxy)
        if err:
            print(f"  curl_cffi: {err}")
            break
        cffi_times.append(dt)
        last_cffi = (http, mit, body)
        print_row(f"curl_cffi[{impersonate}] #{i + 1}", http, dt, mit, body, kind)
        if mit or (http in (0, 403, 503) and gate(body, kind) == "challenge"):
            break

    if last_cffi and gate(last_cffi[2], kind) == "challenge":
        for profile in PROFILES:
            if profile == impersonate:
                continue
            http, mit, body, dt, err = cffi_get(url, merged, ua, profile, proxy)
            if err:
                print(f"  curl_cffi {profile}: {err}")
                break
            print_row(f"curl_cffi[{profile}] retry", http, dt, mit, body, kind)
            if not mit and gate(body, kind) != "challenge":
                impersonate = profile
                last_cffi = (http, mit, body)
                cffi_times.append(dt)
                break

    summarize_times(f"curl_cffi {impersonate}", cffi_times)

    result = {
        "fs_s": round(fs_dt, 3),
        "plain": (plain_http, plain_mit, gate(plain_body, kind)),
        "cffi": None
        if last_cffi is None
        else (last_cffi[0], last_cffi[1], gate(last_cffi[2], kind)),
        "cffi_times": cffi_times,
        "cookie": merged,
        "ua": ua,
        "impersonate": impersonate,
        "fs_html": fs_html,
    }
    if kind == "browse":
        ids = TORRENT_HREF.findall(fs_html)
        result["torrent_id"] = ids[0] if ids else ""
    return result


def main() -> int:
    print("== cf fast path impersonate ==")
    print(f"FS_URL={FS_URL}")
    print(f"PROXY={PROXY or '(none — set PROXY=socks5://127.0.0.1:20001 if FS uses WARP)'}")
    print(f"REPEAT={REPEAT} REPEAT_AFTER_SEC={REPEAT_AFTER_SEC}")
    print(f"kz_session={KZ_SESSION} rt_session={RT_SESSION}")
    print(f"env_uid_pass={'yes' if cookies_from_env() else 'no (reuse FS jar)'}")

    try:
        import curl_cffi  # noqa: F401
    except ImportError:
        print("FAIL: pip install 'curl_cffi>=0.7,<1.0' on this host, then re-run")
        return 2

    ua = ""
    cookie = ""
    impersonate = IMPERSONATE or "chrome131"

    browse = run_url(
        "kinozal browse",
        BROWSE_URL,
        "browse",
        KZ_SESSION,
        cookie,
        ua,
        impersonate,
        PROXY,
    )
    cookie, ua, impersonate = browse["cookie"], browse["ua"], browse["impersonate"]

    torrent_id = browse.get("torrent_id") or os.environ.get("TORRENT_ID", "").strip()
    hash_url = f"{HOST}/get_srv_details.php?id={torrent_id}&action=2" if torrent_id else ""
    hash_row = None
    if hash_url:
        hash_row = run_url(
            "kinozal hash GET",
            hash_url,
            "hash",
            KZ_SESSION,
            cookie,
            ua,
            impersonate,
            PROXY,
        )
        cookie, ua = hash_row["cookie"] or cookie, hash_row["ua"] or ua
    else:
        print("\n== kinozal hash GET skipped (no details.php id on browse) ==")

    empty = run_url(
        "kinozal empty year tail",
        EMPTY_URL,
        "browse",
        KZ_SESSION,
        cookie,
        ua,
        impersonate,
        PROXY,
    )
    cookie, ua = empty["cookie"] or cookie, empty["ua"] or ua

    forum = run_url(
        "rutracker viewforum",
        RUTRACKER_URL,
        "forum",
        RT_SESSION,
        "",
        "",
        impersonate,
        PROXY,
    )

    if REPEAT_AFTER_SEC > 0:
        print(f"\n== wait {REPEAT_AFTER_SEC:.0f}s then browse again ==")
        time.sleep(REPEAT_AFTER_SEC)
        run_url(
            "kinozal browse after wait",
            BROWSE_URL,
            "browse",
            KZ_SESSION,
            cookie,
            ua,
            impersonate,
            PROXY,
        )

    print("\n== verdict ==")
    rows = [
        ("kz browse", browse),
        ("kz hash", hash_row),
        ("kz empty tail", empty),
        ("rt forum", forum),
    ]
    any_cffi_ok = False
    for name, row in rows:
        if not row:
            print(f"  {name:18} skipped")
            continue
        plain = row["plain"]
        cffi = row["cffi"]
        print(
            f"  {name:18} FS={row['fs_s']:.3f}s  "
            f"plain={plain[0]}/{plain[1]}/{plain[2]}  "
            f"cffi={cffi if cffi else 'n/a'}"
        )
        if cffi and cffi[2] not in ("challenge", "error") and not cffi[1]:
            any_cffi_ok = True

    print("  expect: plain urllib is challenge/403; cffi green = TLS+WARP match.")
    print("  STOP: do not wire FetchAsync from this script. Review the table first.")
    return 0 if any_cffi_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
