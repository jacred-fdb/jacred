#!/usr/bin/env python3
"""
Dry-run torrent.by browse pages vs JacRed category map + HTML field shape.

  python3 scripts/dry_run_torrentby_parser.py
  python3 scripts/dry_run_torrentby_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~TorrentBy
"""

from __future__ import annotations

import argparse
import gzip
import io
import json
import os
import re
import ssl
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Dict, List, Optional, Tuple

# Keep in sync with TorrentByCategories.Map
OUR_CATEGORIES: Dict[str, str] = {
    "films": "movie",
    "movies": "movie",
    "serials": "serial",
    "series": "serial",
    "tv": "tvshow",
    "humor": "tvshow",
    "cartoons": "mult",
    "anime": "anime",
    "sport": "sport",
}

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "TorrentBy"

ROW_SPLIT = '<tr class="ttable_col'


def fetch(url: str) -> str:
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Encoding": "gzip"})
    with urllib.request.urlopen(req, context=ctx, timeout=30) as resp:
        raw = resp.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    return raw.decode("utf-8", errors="replace")


def stabilize_dates(html: str) -> str:
    """Replace Сегодня/Вчера with fixed absolute dates for stable fixtures."""
    html = html.replace(">Сегодня</td>", ">2024-07-16</td>")
    html = html.replace(">Вчера</td>", ">2024-07-15</td>")
    return html


def score_page(html: str) -> Tuple[int, int, List[str]]:
    rows = html.split(ROW_SPLIT)[1:]
    ok = 0
    samples: List[str] = []
    for row in rows:
        if "magnet:?xt=urn" not in row:
            continue
        title = re.search(r'<a name="search_select"[^>]*>([^<]+)</a>', row, re.I)
        url = re.search(r'<a name="search_select"[^>]+href="/([0-9]+/[^"]+)"', row, re.I)
        sid = re.search(r'<font color="green">&uarr; ([0-9]+)</font>', row, re.I)
        pir = re.search(r'<font color="red">&darr; ([0-9]+)</font>', row, re.I)
        magnet = re.search(r'href="(magnet:\?xt=[^"]+)"', row, re.I)
        if all([title, url, sid, pir, magnet]):
            ok += 1
            if len(samples) < 2:
                samples.append(re.sub(r"\s+", " ", title.group(1)).strip()[:100])
    return len(rows), ok, samples


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run torrent.by browse HTML vs JacRed")
    p.add_argument("--host", default=os.environ.get("TORRENTBY_HOST", "https://torrent.by"))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    args = p.parse_args(argv)

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)
    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        for stale in fixture_dir.glob("browse_*.html"):
            stale.unlink()

    report = []
    failed = False
    print(f"=== torrent.by parser dry-run ({len(OUR_CATEGORIES)} categories) ===\n")

    for cat, label in OUR_CATEGORIES.items():
        try:
            html = fetch(f"{host}/{cat}/")
        except (urllib.error.URLError, urllib.error.HTTPError) as ex:
            print(f"[FAIL] {cat:<10} fetch error: {ex}")
            failed = True
            continue

        rows, ok, samples = score_page(html)
        rate = round(ok / rows * 100, 1) if rows else 0.0
        valid = "ttable_col" in html and "magnet:?xt" in html and rows > 0 and rate >= 50
        if not valid:
            failed = True
        status = "OK" if valid else "FAIL"
        print(f"[{status}] {cat:<10} {label:<8} rows={rows:3} fields_ok={ok:3} rate={rate:5.1f}%")
        for s in samples:
            print(f"         · {s}")

        report.append({
            "cat": cat, "label": label, "rows": rows, "fields_ok": ok, "rate": rate, "valid": valid, "samples": samples,
        })

        if args.refresh_fixtures:
            path = fixture_dir / f"browse_{cat}.html"
            path.write_text(stabilize_dates(html), encoding="utf-8")
            print(f"         wrote {path.relative_to(REPO_ROOT)}")

    print()
    if args.json_out:
        Path(args.json_out).parent.mkdir(parents=True, exist_ok=True)
        Path(args.json_out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Wrote {args.json_out}")

    if failed:
        print("Dry-run FAILED.", file=sys.stderr)
        return 2

    print("Dry-run OK. Run:")
    print("  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~TorrentBy")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
