#!/usr/bin/env python3
"""
Dry-run NNMClub portal pages vs JacRed category map + HTML field shape.

  python3 scripts/dry_run_nnmclub_parser.py
  python3 scripts/dry_run_nnmclub_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~NNMClub
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
CATEGORIES_CS = REPO_ROOT / "Infrastructure" / "Trackers" / "NNMClub" / "NNMClubCategories.cs"
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "NNMClub"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

ENTRY_RE = re.compile(
    r'\["(\d+)"\]\s*=\s*new\(\)\s*\{\s*'
    r"Types\s*=\s*new\[\]\s*\{\s*([^}]+)\}\s*,\s*"
    r"TitleKind\s*=\s*NNMClubTitleKind\.(\w+)"
    r"(?:,\s*RequireMultInRow\s*=\s*(true|false))?"
    r"(?:,\s*SkipPdfInTitle\s*=\s*(true|false))?\s*\}",
    re.S,
)

PLINE_RE = re.compile(r'<table width="100%" class="pline">', re.I)
TITLE_OK = re.compile(r"NNM-Club</title>", re.I)


def parse_map(path: Path) -> Dict[str, Tuple[List[str], str]]:
    text = path.read_text(encoding="utf-8")
    out: Dict[str, Tuple[List[str], str]] = {}
    for m in ENTRY_RE.finditer(text):
        fid = m.group(1)
        types = [t.strip().strip('"') for t in m.group(2).split(",") if t.strip()]
        kind = m.group(3)
        out[fid] = (types, kind)
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
    # NNMClub portal is Windows-1251
    try:
        return raw.decode("cp1251")
    except UnicodeDecodeError:
        return raw.decode("utf-8", errors="replace")


def score_page(html: str) -> Tuple[int, int, List[str]]:
    flat = re.sub(r"[\n\r\t]", "", html)
    parts = PLINE_RE.split(flat)
    rows = parts[1:] if len(parts) > 1 else []
    ok = 0
    samples: List[str] = []
    for row in rows:
        if "magnet:" not in row:
            continue
        url = re.search(r'<a class="pgenmed" href="(viewtopic\.php[^"]+)"', row, re.I)
        title = re.search(r">([^<]+)</a></h2></td>", row, re.I)
        sid = re.search(r'title="Раздаю[щш]их">&nbsp;([0-9]+)</span>', row, re.I)
        pir = re.search(r'title="Качают">&nbsp;([0-9]+)</span>', row, re.I)
        size = re.search(r'<span class="pcomm bold">([^<]+)</span>', row, re.I)
        magnet = re.search(r'"(magnet:[^"]+)"', row, re.I)
        if all([url, title, sid, pir, size, magnet]):
            ok += 1
            if len(samples) < 2:
                samples.append(re.sub(r"\s+", " ", title.group(1)).strip()[:100])
    return len(rows), ok, samples


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run NNMClub portal HTML vs JacRed")
    p.add_argument("--host", default=os.environ.get("NNMCLUB_HOST", "https://nnmclub.to"))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    args = p.parse_args(argv)

    mp = parse_map(CATEGORIES_CS)
    if not mp:
        print(f"[FAIL] could not parse category map from {CATEGORIES_CS}", file=sys.stderr)
        return 1

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)

    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        for stale in fixture_dir.glob("portal_c*.html"):
            stale.unlink()

    report = []
    failed = False
    print(f"=== NNMClub parser dry-run ({len(mp)} categories) ===\n")

    for fid in sorted(mp, key=int):
        types, kind = mp[fid]
        url = f"{host}/forum/portal.php?c={fid}"
        try:
            html = fetch(url)
        except (urllib.error.URLError, urllib.error.HTTPError) as ex:
            print(f"[FAIL] cat={fid:<3} fetch error: {ex}")
            failed = True
            continue

        if not TITLE_OK.search(html):
            print(f"[FAIL] cat={fid:<3} missing NNM-Club</title> marker")
            failed = True
            continue

        rows, ok, samples = score_page(html)
        rate = round(ok / rows * 100, 1) if rows else 0.0
        # Cat 7 (kids) filters many non-cartoon rows — lower bar for field-shape OK rate.
        min_rate = 20 if fid == "7" else 40
        valid = rows > 0 and rate >= min_rate
        if not valid:
            failed = True

        status = "OK" if valid else "FAIL"
        print(
            f"[{status}] cat={fid:<3} types={types} kind={kind} "
            f"rows={rows} ok={ok} rate={rate}%"
        )
        for s in samples:
            print(f"         sample: {s}")

        if args.refresh_fixtures and html:
            out = fixture_dir / f"portal_c{fid}.html"
            out.write_text(html, encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")

        report.append(
            {
                "id": fid,
                "types": types,
                "titleKind": kind,
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
