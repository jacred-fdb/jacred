#!/usr/bin/env python3
"""Live spike: fetch() inside an already-solved Chromium, no page.goto.

FlareSolverr has no execute_script API. This script:

1. Probes whether the FlareSolverr Chrome exposes CDP (9222 / docker args).
2. If Playwright is installed, one goto (solve/reuse) then page.evaluate(fetch)
   on the same origin — research only, not a new compose service.
3. Otherwise prints SKIP. Do not treat SKIP as a reason to add a second browser
   container.

  python3 scripts/cf_fastpath_inpage_check.py
  ssh master.jacred.stream 'python3 -' < scripts/cf_fastpath_inpage_check.py
"""

from __future__ import annotations

import json
import os
import re
import socket
import ssl
import subprocess
import sys
import time
import urllib.request
from typing import Any

FS_URL = os.environ.get("FS_URL", "http://127.0.0.1:8191/v1")
SESSION = os.environ.get("FS_SESSION", "jacred-kinozal_guru")
PROXY = os.environ.get("PROXY", os.environ.get("PROXY_URL", "")).strip()
HOST = os.environ.get("KINOZAL_HOST", "https://kinozal.guru").rstrip("/")
BROWSE_URL = os.environ.get("BROWSE_URL", f"{HOST}/browse.php?c=22&page=0")
MAX_TIMEOUT_MS = int(os.environ.get("MAX_TIMEOUT_MS", "180000"))
CDP_HOSTS = os.environ.get("CDP_HOSTS", "127.0.0.1:9222,127.0.0.1:9223").split(",")

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

TORRENT_HREF = re.compile(r"""href=["']/details\.php\?id=(\d+)["']""", re.I)


def fs_get(url: str) -> tuple[dict[str, Any], float]:
    payload = json.dumps(
        {
            "cmd": "request.get",
            "session": SESSION,
            "url": url,
            "maxTimeout": MAX_TIMEOUT_MS,
        }
    ).encode()
    req = urllib.request.Request(
        FS_URL, data=payload, headers={"Content-Type": "application/json"}
    )
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=MAX_TIMEOUT_MS / 1000 + 30, context=CTX) as resp:
            root = json.loads(resp.read().decode())
    except Exception as exc:
        return {"status": "error", "message": f"{type(exc).__name__}: {exc}"}, time.time() - t0
    return root, time.time() - t0


def port_open(spec: str) -> bool:
    spec = spec.strip()
    if ":" not in spec:
        return False
    host, _, port_s = spec.rpartition(":")
    try:
        port = int(port_s)
    except ValueError:
        return False
    sock = socket.socket()
    sock.settimeout(0.4)
    try:
        sock.connect((host, port))
        return True
    except OSError:
        return False
    finally:
        sock.close()


def docker_chrome_args() -> str:
    try:
        out = subprocess.check_output(
            ["docker", "inspect", "-f", "{{json .Config.Env}} {{json .Args}} {{.Config.Cmd}}", "flaresolverr"],
            stderr=subprocess.DEVNULL,
            text=True,
            timeout=8,
        )
        return out.strip()[:800]
    except Exception as exc:
        return f"(docker inspect skipped: {type(exc).__name__})"


def html_gate(html: str) -> str:
    low = (html or "").lower()
    if "just a moment" in low or "_cf_chl_opt" in (html or ""):
        return "challenge"
    if "t_peer" in (html or "") and TORRENT_HREF.search(html or ""):
        return "listing"
    if "Нет активных раздач" in (html or ""):
        return "empty_search"
    return "other"


def try_playwright(browse: str, hash_url: str, proxy: str) -> int:
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("Playwright: not installed (pip install playwright && playwright install chromium)")
        return 2

    launch: dict[str, Any] = {"headless": True}
    if proxy:
        launch["proxy"] = {"server": proxy}

    print("Playwright: one goto, then page.evaluate(fetch) on same origin")
    with sync_playwright() as p:
        browser = p.chromium.launch(**launch)
        page = browser.new_page()
        t0 = time.time()
        page.goto(browse, wait_until="domcontentloaded", timeout=180_000)
        goto_s = time.time() - t0
        html = page.content()
        print(f"  goto browse {goto_s:.3f}s gate={html_gate(html)} len={len(html)}")

        fetch_js = """async (url) => {
            const r = await fetch(url, { credentials: 'include' });
            const text = await r.text();
            return { status: r.status, len: text.length, text };
        }"""

        urls = [("browse_again", browse)]
        if hash_url:
            urls.append(("hash", hash_url))

        times: list[float] = []
        for name, url in urls:
            t1 = time.time()
            try:
                data = page.evaluate(fetch_js, url)
            except Exception as exc:
                print(f"  evaluate {name}: {type(exc).__name__}: {exc}")
                continue
            dt = time.time() - t1
            times.append(dt)
            text = data.get("text") or ""
            print(
                f"  evaluate fetch {name:14} {dt:.3f}s http={data.get('status')} "
                f"gate={html_gate(text)} len={data.get('len')}"
            )
        browser.close()
        if times:
            print(f"  in-page fetch min={min(times):.3f}s max={max(times):.3f}s vs goto {goto_s:.3f}s")
            return 0
    return 1


def main() -> int:
    print("== cf fast path in-page ==")
    print(f"FS_URL={FS_URL} session={SESSION}")
    print(f"PROXY={PROXY or '(none)'}")
    print(f"CDP probe: {', '.join(h.strip() for h in CDP_HOSTS)}")

    cdp_hits = [h.strip() for h in CDP_HOSTS if port_open(h)]
    print(f"  open CDP ports: {cdp_hits or '(none)'}")
    print(f"  flaresolverr inspect: {docker_chrome_args()}")

    root, fs_dt = fs_get(BROWSE_URL)
    sol = root.get("solution") or {}
    html = sol.get("response") or ""
    print(
        f"  FS request.get {fs_dt:.3f}s status={root.get('status')} "
        f"http={sol.get('status')} gate={html_gate(html)} len={len(html)}"
    )
    ids = TORRENT_HREF.findall(html)
    hash_url = f"{HOST}/get_srv_details.php?id={ids[0]}&action=2" if ids else ""

    if cdp_hits:
        print("  CDP is open, but this script has no websocket client.")
        print("  In-page fetch in the FlareSolverr tab needs Runtime.evaluate over CDP.")
        print("  Next: enable a tiny CDP eval in a later spike, still no extra container.")

    rc = try_playwright(BROWSE_URL, hash_url, PROXY)
    if rc == 2:
        print("SKIP: no CDP eval client and no Playwright — in-page not measured this host.")
        print("  Do not add a Playwright/Camoufox container for ParseAll.")
        return 0
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
