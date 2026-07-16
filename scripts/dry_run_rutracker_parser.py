#!/usr/bin/env python3
"""
Dry-run Rutracker forum listings vs JacRed category map + HTML field shape.

Fetches a representative sample (not all ~211 forums):
  Movie, Serial, NonStandard(anime), sport, doc, tvshow.

  python3 scripts/dry_run_rutracker_parser.py
  python3 scripts/dry_run_rutracker_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Rutracker
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
CATEGORIES_CS = REPO_ROOT / "Infrastructure" / "Trackers" / "Rutracker" / "RutrackerCategories.cs"
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Rutracker"

# Representative forums: one per TitleKind + sport + doc + tvshow
SAMPLE_FORUMS: Dict[str, str] = {
    "1950": "Movie / foreign films",
    "842": "Serial / foreign serials",
    "1105": "NonStandard / anime",
    "1392": "NonStandard / sport (former orphan)",
    "709": "Movie / documovie",
    "24": "NonStandard / tvshow",
}

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

ENTRY_RE = re.compile(
    r'\["(\d+)"\]\s*=\s*new\(\)\s*\{\s*'
    r"Types\s*=\s*new\[\]\s*\{\s*([^}]+)\}\s*,\s*"
    r"TitleKind\s*=\s*RutrackerTitleKind\.(\w+)\s*,\s*"
    r"QuickParse\s*=\s*(true|false)\s*\}",
    re.S,
)

ROW_SPLIT = 'class="torTopic"'


def parse_map(path: Path) -> Dict[str, Tuple[List[str], str, bool]]:
    text = path.read_text(encoding="utf-8")
    out: Dict[str, Tuple[List[str], str, bool]] = {}
    for m in ENTRY_RE.finditer(text):
        fid = m.group(1)
        types = [t.strip().strip('"') for t in m.group(2).split(",") if t.strip()]
        kind = m.group(3)
        quick = m.group(4) == "true"
        out[fid] = (types, kind, quick)
    return out


def fetch(url: str) -> str:
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Encoding": "gzip"})
    with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
        ctype = (resp.headers.get("Content-Type") or "").lower()
        raw = resp.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    # Rutracker serves Windows-1251; normalize fixtures to UTF-8.
    if "1251" in ctype or b'charset="Windows-1251"' in raw[:2000] or b"charset=Windows-1251" in raw[:2000]:
        return raw.decode("cp1251")
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return raw.decode("cp1251")


def score_page(html: str) -> Tuple[int, int, List[str]]:
    rows = html.split(ROW_SPLIT)[1:]
    ok = 0
    samples: List[str] = []
    for row in rows:
        tid = re.search(r'<a id="tt-([0-9]+)"', row, re.I)
        title = re.search(r'<a id="tt-[0-9]+"[^>]+>([^\n\r]+)</a>', row, re.I)
        sid = re.search(r'<span class="seedmed"[^>]*><b>([0-9]+)</b>', row, re.I)
        pir = re.search(r'<span class="leechmed"[^>]*><b>([0-9]+)</b>', row, re.I)
        size = re.search(r'dl-stub">([^<]+)</a>', row, re.I)
        time = re.search(r"<p>([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})</p>", row)
        if all([tid, title, sid, pir, size, time]):
            ok += 1
            if len(samples) < 2:
                t = re.sub(r"<[^>]+>", "", title.group(1))
                samples.append(re.sub(r"\s+", " ", t).strip()[:100])
    return len(rows), ok, samples


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run Rutracker forum HTML vs JacRed")
    p.add_argument("--host", default=os.environ.get("RUTRACKER_HOST", "https://rutracker.org"))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    args = p.parse_args(argv)

    mp = parse_map(CATEGORIES_CS)
    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)

    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        for stale in fixture_dir.glob("forum_*.html"):
            stale.unlink()

    report = []
    failed = False
    print(f"=== Rutracker parser dry-run ({len(SAMPLE_FORUMS)} sample forums) ===\n")

    for fid, label in SAMPLE_FORUMS.items():
        if fid not in mp:
            print(f"[FAIL] f={fid} not in map ({label})")
            failed = True
            continue

        types, kind, quick = mp[fid]
        url = f"{host}/forum/viewforum.php?f={fid}"
        try:
            html = fetch(url)
        except (urllib.error.URLError, urllib.error.HTTPError) as ex:
            print(f"[FAIL] f={fid:<5} fetch error: {ex}")
            failed = True
            continue

        rows, ok, samples = score_page(html)
        rate = round(ok / rows * 100, 1) if rows else 0.0
        valid = ROW_SPLIT in html and rows > 0 and rate >= 40
        if not valid:
            failed = True

        status = "OK" if valid else "FAIL"
        print(
            f"[{status}] f={fid:<5} {label:<40} "
            f"types={types} kind={kind} quick={quick} "
            f"rows={rows} ok={ok} rate={rate}%"
        )
        for s in samples:
            print(f"         sample: {s}")

        if args.refresh_fixtures and valid:
            out = fixture_dir / f"forum_{fid}.html"
            out.write_text(html, encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")

        report.append(
            {
                "id": fid,
                "label": label,
                "types": types,
                "titleKind": kind,
                "quickParse": quick,
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
