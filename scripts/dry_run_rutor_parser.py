#!/usr/bin/env python3
"""
Dry-run Rutor browse pages vs JacRed category map + HTML field shape.

  python3 scripts/dry_run_rutor_parser.py
  python3 scripts/dry_run_rutor_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Rutor
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

REPO_ROOT = Path(__file__).resolve().parents[1]
CATEGORIES_CS = REPO_ROOT / "Infrastructure" / "Trackers" / "Rutor" / "RutorCategories.cs"
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Rutor"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

ENTRY_RE = re.compile(
    r'\["(\d+)"\]\s*=\s*new\(\)\s*\{\s*'
    r"Types\s*=\s*new\[\]\s*\{\s*([^}]+)\}\s*,\s*"
    r"TitleKind\s*=\s*RutorTitleKind\.(\w+)"
    r"(?:,\s*RequireUkrInTitle\s*=\s*(true|false))?\s*\}",
    re.S,
)

ROW_RE = re.compile(r'<tr class="(gai|tum)">', re.I)


def parse_map(path: Path) -> Dict[str, Tuple[List[str], str, bool]]:
    text = path.read_text(encoding="utf-8")
    out: Dict[str, Tuple[List[str], str, bool]] = {}
    for m in ENTRY_RE.finditer(text):
        fid = m.group(1)
        types = [t.strip().strip('"') for t in m.group(2).split(",") if t.strip()]
        kind = m.group(3)
        ukr = m.group(4) == "true"
        out[fid] = (types, kind, ukr)
    return out


def fetch(url: str) -> str:
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Encoding": "gzip"})
    with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
        raw = resp.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return raw.decode("cp1251")


def score_page(html: str) -> Tuple[int, int, List[str]]:
    parts = ROW_RE.split(html)
    # split with capturing group: [before, gai|tum, row, gai|tum, row, ...]
    rows = parts[2::2] if len(parts) > 2 else []
    ok = 0
    samples: List[str] = []
    for row in rows:
        if "magnet:?xt=urn" not in row:
            continue
        url = re.search(r'<a href="/(torrent/[^"]+)">', row, re.I)
        title = re.search(r'<a href="/torrent/[^"]+">([^<]+)</a>', row, re.I)
        sid = re.search(r'<span class="green"><img [^>]+>&nbsp;([0-9]+)</span>', row, re.I)
        pir = re.search(r'<span class="red">&nbsp;([0-9]+)</span>', row, re.I)
        size = re.search(r'<td align="right">([^<]+)</td>', row, re.I)
        magnet = re.search(r'href="(magnet:\?xt=[^"]+)"', row, re.I)
        if all([url, title, sid, pir, size, magnet]):
            ok += 1
            if len(samples) < 2:
                samples.append(re.sub(r"\s+", " ", title.group(1)).strip()[:100])
    return len(rows), ok, samples


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run Rutor browse HTML vs JacRed")
    p.add_argument("--host", default=os.environ.get("RUTOR_HOST", "http://rutor.info"))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    args = p.parse_args(argv)

    mp = parse_map(CATEGORIES_CS)
    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)

    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        for stale in fixture_dir.glob("browse_*.html"):
            stale.unlink()

    report = []
    failed = False
    print(f"=== Rutor parser dry-run ({len(mp)} categories) ===\n")

    for fid in sorted(mp, key=int):
        types, kind, ukr = mp[fid]
        url = f"{host}/browse/0/{fid}/0/0"
        try:
            html = fetch(url)
        except (urllib.error.URLError, urllib.error.HTTPError) as ex:
            print(f"[FAIL] cat={fid:<3} fetch error: {ex}")
            failed = True
            continue

        rows, ok, samples = score_page(html)
        rate = round(ok / rows * 100, 1) if rows else 0.0
        valid = rows > 0 and rate >= 50
        if not valid:
            failed = True

        status = "OK" if valid else "FAIL"
        print(
            f"[{status}] cat={fid:<3} types={types} kind={kind} ukr={ukr} "
            f"rows={rows} ok={ok} rate={rate}%"
        )
        for s in samples:
            print(f"         sample: {s}")

        if args.refresh_fixtures and valid:
            out = fixture_dir / f"browse_{fid}.html"
            out.write_text(html, encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")

        report.append(
            {
                "id": fid,
                "types": types,
                "titleKind": kind,
                "requireUkr": ukr,
                "rows": rows,
                "ok": ok,
                "rate": rate,
                "valid": valid,
                "samples": samples,
            }
        )

    if args.json_out:
        Path(args.json_out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
