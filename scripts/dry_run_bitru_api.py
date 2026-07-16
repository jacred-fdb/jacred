#!/usr/bin/env python3
"""
Dry-run BitRu official API vs JacRed category map + JSON field shape.

  python3 scripts/dry_run_bitru_api.py
  python3 scripts/dry_run_bitru_api.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Bitru
"""

from __future__ import annotations

import argparse
import json
import os
import ssl
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Bitru"

REQUEST_CATEGORIES = ["movie", "serial", "video"]

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)


def api_post(host: str, params: dict) -> dict:
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    body = urllib.parse.urlencode(
        {"get": "torrents", "json": json.dumps(params, ensure_ascii=False)}
    ).encode()
    req = urllib.request.Request(
        f"{host.rstrip('/')}/api.php",
        data=body,
        headers={
            "User-Agent": UA,
            "Content-Type": "application/x-www-form-urlencoded",
            "Accept-Encoding": "identity",
        },
    )
    with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
        return json.loads(resp.read().decode("utf-8"))


def score_response(data: dict) -> Tuple[int, int, List[str], Optional[str]]:
    if isinstance(data.get("error"), str) and data["error"]:
        return 0, 0, [], data["error"]

    items = (data.get("result") or {}).get("items") or []
    ok = 0
    samples: List[str] = []
    for wrap in items:
        it = wrap.get("item") or {}
        tor = it.get("torrent") or {}
        info = it.get("info") or {}
        tmpl = it.get("template") or {}
        if not isinstance(tor, dict) or not isinstance(info, dict) or not isinstance(tmpl, dict):
            continue
        if not tor.get("id") or not info.get("name") or not tmpl.get("category"):
            continue
        file_url = tor.get("file") or ""
        if file_url and "api.php?download=" not in str(file_url):
            continue
        ok += 1
        if len(samples) < 2:
            samples.append(str(info.get("name", ""))[:100])
    return len(items), ok, samples, None


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run BitRu API JSON vs JacRed")
    p.add_argument("--host", default=os.environ.get("BITRU_HOST", "https://bitru.org"))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--limit", type=int, default=50)
    args = p.parse_args(argv)

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)
    limit = min(100, max(1, args.limit))

    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        for stale in fixture_dir.glob("api_*.json"):
            stale.unlink()

    failed = False
    print(f"=== BitRu API dry-run (limit={limit}) ===\n")

    fetches: List[Tuple[str, Dict[str, Any], str]] = [
        ("movie+serial", {"limit": limit, "category": ["movie", "serial"]}, "api_movie_serial_page1.json"),
        ("video", {"limit": limit, "category": ["video"]}, "api_video_page1.json"),
    ]

    for label, params, fixture_name in fetches:
        time.sleep(0.3)
        try:
            data = api_post(host, params)
        except (urllib.error.URLError, urllib.error.HTTPError, json.JSONDecodeError) as ex:
            print(f"[FAIL] {label}: fetch error: {ex}")
            failed = True
            continue

        rows, ok, samples, err = score_response(data)
        if err:
            print(f"[FAIL] {label}: API error: {err}")
            failed = True
            continue

        rate = round(ok / rows * 100, 1) if rows else 0.0
        valid = rows > 0 and rate >= 50
        if not valid:
            failed = True

        status = "OK" if valid else "FAIL"
        print(f"[{status}] {label:<14} rows={rows} ok={ok} rate={rate}%")
        for s in samples:
            print(f"         sample: {s}")

        if args.refresh_fixtures and valid:
            out = fixture_dir / fixture_name
            out.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")

            # optional page2 via before_date
            before = (data.get("result") or {}).get("before_date")
            if before and label == "movie+serial":
                time.sleep(0.3)
                try:
                    page2 = api_post(
                        host,
                        {
                            "limit": min(20, limit),
                            "category": ["movie", "serial"],
                            "before_date": str(before),
                        },
                    )
                    r2, o2, _, e2 = score_response(page2)
                    if not e2 and r2 > 0:
                        out2 = fixture_dir / "api_movie_serial_page2.json"
                        out2.write_text(json.dumps(page2, ensure_ascii=False, indent=2), encoding="utf-8")
                        print(f"         wrote {out2.relative_to(REPO_ROOT)} (page2 rows={r2} ok={o2})")
                except Exception as ex:
                    print(f"         page2 skipped: {ex}")

    print()
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
