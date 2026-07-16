#!/usr/bin/env python3
"""
Dry-run Lostfilm /new/ HTML shape vs collector expectations.

  python3 scripts/dry_run_lostfilm_parser.py
  python3 scripts/dry_run_lostfilm_parser.py --refresh-fixtures

Live fetch needs cookie for V/torrent pages; /new/ often works without:
  LOSTFILM_COOKIE='...' python3 scripts/dry_run_lostfilm_parser.py --refresh-fixtures

Never commit live cookies.
"""

from __future__ import annotations

import argparse
import gzip
import io
import json
import os
import re
import ssl
import urllib.error
import urllib.request
from pathlib import Path
from typing import List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Lostfilm"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

EPISODE_RE = re.compile(
    r'<a\s[^>]*href="[^"]*?(/series/([^/"]+)/season_(\d+)/episode_(\d+)/)[^"]*"[^>]*>([\s\S]*?)</a>',
    re.I,
)
SINFO_RE = re.compile(r"(\d+)\s*сезон\s*(\d+)\s*серия", re.I)
DATE_RE = re.compile(r"(\d{2}\.\d{2}\.\d{4})")
HOR_RE = re.compile(r'class="hor-breaker dashed"', re.I)
MARK = "LostFilm.TV"


def fetch(url: str, cookie: str = "") -> str:
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    headers = {"User-Agent": UA, "Accept-Encoding": "gzip"}
    if cookie:
        headers["Cookie"] = cookie
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
        raw = resp.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    return raw.decode("utf-8", errors="replace")


def score_new_page(html: str) -> Tuple[int, int, int, List[str]]:
    eps = 0
    ok = 0
    samples: List[str] = []
    seen = set()
    for m in EPISODE_RE.finditer(html):
        path = m.group(1).lstrip("/")
        if path in seen:
            continue
        seen.add(path)
        eps += 1
        block = m.group(5)
        if SINFO_RE.search(block) and DATE_RE.search(block):
            ok += 1
            if len(samples) < 3:
                sm = SINFO_RE.search(block)
                samples.append(f"{m.group(2)} {sm.group(0) if sm else ''}".strip()[:100])
    hors = len(HOR_RE.findall(html))
    return eps, ok, hors, samples


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run Lostfilm /new/ HTML")
    p.add_argument("--host", default=os.environ.get("LOSTFILM_HOST", "https://www.lostfilm.tv"))
    p.add_argument("--cookie", default=os.environ.get("LOSTFILM_COOKIE", ""))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    args = p.parse_args(argv)

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)
    fixture_dir.mkdir(parents=True, exist_ok=True)

    url = f"{host}/new/"
    print(f"=== Lostfilm dry-run ===\nGET {url}\n")

    try:
        html = fetch(url, args.cookie)
    except (urllib.error.URLError, urllib.error.HTTPError) as ex:
        print(f"[FAIL] fetch: {ex}")
        return 1

    if MARK not in html:
        print("[FAIL] missing LostFilm.TV marker")
        return 1

    eps, ok, hors, samples = score_new_page(html)
    rate = round(ok / eps * 100, 1) if eps else 0.0
    valid = eps > 0 and rate >= 50
    status = "OK" if valid else "FAIL"
    print(f"[{status}] episode_links={eps} ok={ok} rate={rate}% hor-breaker-rows={hors}")
    for s in samples:
        print(f"         sample: {s}")

    if args.refresh_fixtures:
        out = fixture_dir / "new_page1.html"
        out.write_text(html, encoding="utf-8")
        print(f"wrote {out.relative_to(REPO_ROOT)}")

    report = {
        "episodes": eps,
        "ok": ok,
        "rate": rate,
        "horBreaker": hors,
        "valid": valid,
        "samples": samples,
    }
    if args.json_out:
        Path(args.json_out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    return 0 if valid else 1


if __name__ == "__main__":
    raise SystemExit(main())
