#!/usr/bin/env python3
"""
Dry-run Rutracker forum listings vs JacRed category map + HTML field shape.

Fetches a representative sample (not the full map):
  Movie, Serial, NonStandard(anime), sport, doc, tvshow.

  python3 scripts/dry_run_rutracker_parser.py
  python3 scripts/dry_run_rutracker_parser.py --check-snapshot
  python3 scripts/dry_run_rutracker_parser.py --probe-tree
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
DEFAULT_SNAPSHOT = DEFAULT_FIXTURE_DIR / "forum_tree_snapshot.json"
FS_URL = os.environ.get("FS_URL", "http://127.0.0.1:8191/v1")

# Parent forums whose viewforum subforum table is the real leaf catalog.
# index.php sf_title is incomplete (under 119 it only shows 1171).
VIDEO_TREE_PARENTS = (
    "119", "2366", "189", "2100", "911", "718", "4", "921", "7", "22",
    "33", "9", "81", "812", "46", "314", "24", "255",
)

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


def flaresolverr_get(url: str, session: str, timeout: int = 180) -> str:
    payload = json.dumps(
        {"cmd": "request.get", "url": url, "session": session, "maxTimeout": 120000}
    ).encode()
    req = urllib.request.Request(
        FS_URL, data=payload, headers={"Content-Type": "application/json"}, method="POST"
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read().decode())
    sol = data.get("solution") or {}
    return sol.get("response") or ""


def flaresolverr_session() -> str:
    payload = json.dumps({"cmd": "sessions.create"}).encode()
    req = urllib.request.Request(
        FS_URL, data=payload, headers={"Content-Type": "application/json"}, method="POST"
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        data = json.loads(resp.read().decode())
    session = data.get("session")
    if not session:
        raise RuntimeError(f"FlareSolverr sessions.create failed: {data}")
    return session


FORUMLINK_RE = re.compile(
    r'<h4 class="forumlink"><a href="viewforum\.php\?f=(\d+)">([^<]+)</a>',
    re.I,
)
PAGE_OF_RE = re.compile(r"Страница <b>1</b> из <b>([0-9]+)</b>")


def parse_children(html: str) -> List[Tuple[str, str]]:
    return [(m.group(1), m.group(2).strip()) for m in FORUMLINK_RE.finditer(html)]


def page_count(html: str) -> int:
    m = PAGE_OF_RE.search(html)
    return int(m.group(1)) if m else 1


def check_snapshot(mp: Dict[str, Tuple[List[str], str, bool]], path: Path) -> bool:
    if not path.is_file():
        print(f"[FAIL] snapshot missing: {path}")
        return False

    data = json.loads(path.read_text(encoding="utf-8"))
    forums = data.get("forums") or []
    p0 = sorted({f["id"] for f in forums if f.get("priority") == "p0"})
    print(f"=== snapshot {path.relative_to(REPO_ROOT)} p0={len(p0)} captured={data.get('captured')} ===")
    ok = True
    if len(p0) != 14:
        print(f"[FAIL] expected 14 P0 forums, got {len(p0)}")
        ok = False
    for fid in p0:
        if fid not in mp:
            print(f"[FAIL] P0 f={fid} not in RutrackerCategories.Map")
            ok = False
            continue
        _types, _kind, quick = mp[fid]
        if not quick:
            print(f"[FAIL] P0 f={fid} is in map but QuickParse=false")
            ok = False
        else:
            print(f"[OK]   f={fid:<5} QuickParse types={mp[fid][0]}")
    return ok


def probe_tree(mp: Dict[str, Tuple[List[str], str, bool]], host: str) -> int:
    print(f"=== live forum tree via FlareSolverr {FS_URL} ===")
    session = flaresolverr_session()
    missing_p0 = []
    unknown = []
    for parent in VIDEO_TREE_PARENTS:
        url = f"{host}/forum/viewforum.php?f={parent}"
        try:
            html = flaresolverr_get(url, session)
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError) as ex:
            print(f"[FAIL] parent f={parent} fetch: {ex}")
            return 1
        kids = parse_children(html)
        pages = page_count(html)
        in_map = parent in mp
        print(f"parent f={parent:<5} inMap={in_map} pages={pages} children={len(kids)}")
        for fid, name in kids:
            mapped = fid in mp
            mark = "IN" if mapped else "MISS"
            print(f"  {mark:4} f={fid:<5} {name}")
            if not mapped:
                unknown.append((fid, name, parent))

    p0_from_snapshot = []
    snap = DEFAULT_SNAPSHOT
    if snap.is_file():
        data = json.loads(snap.read_text(encoding="utf-8"))
        p0_from_snapshot = [f["id"] for f in data.get("forums") or [] if f.get("priority") == "p0"]
        for fid in p0_from_snapshot:
            if fid not in mp:
                missing_p0.append(fid)

    if missing_p0:
        print(f"[FAIL] P0 still missing from map: {missing_p0}")
        return 1
    print(f"unknown children not in map: {len(unknown)} (P1/meta; not a P0 failure)")
    return 0


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
    p.add_argument(
        "--check-snapshot",
        action="store_true",
        help="Offline: P0 forums in forum_tree_snapshot.json must be QuickParse in the C# map",
    )
    p.add_argument(
        "--probe-tree",
        action="store_true",
        help="Live BFS of video parent viewforum pages via FlareSolverr (FS_URL)",
    )
    p.add_argument("--snapshot", default=str(DEFAULT_SNAPSHOT))
    args = p.parse_args(argv)

    mp = parse_map(CATEGORIES_CS)
    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)
    snapshot = Path(args.snapshot)

    if args.check_snapshot or args.probe_tree:
        failed_snap = not check_snapshot(mp, snapshot)
        if args.probe_tree:
            rc = probe_tree(mp, host)
            return 1 if failed_snap or rc else 0
        return 1 if failed_snap else 0

    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        for stale in fixture_dir.glob("forum_*.html"):
            stale.unlink()

    report = []
    failed = False
    if not check_snapshot(mp, snapshot):
        failed = True
    print()
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
