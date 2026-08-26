#!/usr/bin/env python3
"""
Dry-run SubsPlease JSON API vs JacRed 1080-only mapping.

  python3 scripts/dry_run_subsplease_parser.py
  python3 scripts/dry_run_subsplease_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~SubsPlease
"""

from __future__ import annotations

import argparse
import gzip
import io
import json
import re
import ssl
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "SubsPlease"
DEFAULT_HOST = "https://subsplease.org"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

XL_RE = re.compile(r"[?&]xl=(\d+)", re.I)
BTIH_RE = re.compile(r"xt=urn:btih:([A-Za-z0-9]{32,40})", re.I)
SID_RE = re.compile(
    r'<table[^>]*id=["\']show-release-table["\'][^>]*\bsid=["\'](\d+)["\']',
    re.I,
)
SHOW_LINK_RE = re.compile(r'href=["\']/shows/([^"\'/]+)/?["\']', re.I)


def fetch(url: str, accept_json: bool = False) -> str:
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    headers = {"User-Agent": UA, "Accept-Encoding": "gzip"}
    if accept_json:
        headers["Accept"] = "application/json"
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
        raw = resp.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    return raw.decode("utf-8", errors="replace")


def is_batch(episode: str) -> bool:
    return "-" in (episode or "")


def pick_1080(downloads: List[dict]) -> Optional[dict]:
    for d in downloads or []:
        if str(d.get("res", "")) == "1080" and d.get("magnet"):
            return d
    return None


def score_release(key: str, rel: dict, page_fallback: str = "") -> Optional[dict]:
    dl = pick_1080(rel.get("downloads") or [])
    if not dl:
        return None
    show = (rel.get("show") or "").strip()
    episode = (rel.get("episode") or "").strip()
    page = (rel.get("page") or page_fallback or "").strip()
    if not show or not episode or not page:
        return None
    magnet = dl["magnet"]
    xl_m = XL_RE.search(magnet)
    btih_m = BTIH_RE.search(magnet)
    batch = is_batch(episode)
    title = f"[SubsPlease] {show} - {episode} (1080p)"
    if batch:
        title += " [Batch]"
    return {
        "key": key,
        "show": show,
        "episode": episode,
        "page": page,
        "batch": batch,
        "title": title,
        "xl": int(xl_m.group(1)) if xl_m else None,
        "infohash": btih_m.group(1).upper() if btih_m else None,
        "torrent": dl.get("torrent"),
        "image_url": rel.get("image_url"),
        "xdcc": rel.get("xdcc"),
        "release_date": rel.get("release_date"),
    }


def parse_latest(obj: Any) -> Tuple[List[dict], int]:
    if not isinstance(obj, dict):
        return [], 0
    kept: List[dict] = []
    dropped = 0
    for key, rel in obj.items():
        if not isinstance(rel, dict):
            dropped += 1
            continue
        row = score_release(key, rel)
        if row:
            kept.append(row)
        else:
            dropped += 1
    return kept, dropped


def parse_show(obj: Any, page: str) -> Tuple[List[dict], int]:
    kept: List[dict] = []
    dropped = 0
    if not isinstance(obj, dict):
        return kept, dropped
    for section in ("batch", "episode"):
        bag = obj.get(section) or {}
        if not isinstance(bag, dict):
            continue
        for key, rel in bag.items():
            if not isinstance(rel, dict):
                dropped += 1
                continue
            row = score_release(key, rel, page_fallback=page)
            if row:
                row["section"] = section
                kept.append(row)
            else:
                dropped += 1
    return kept, dropped


def refresh_fixtures(host: str, fixture_dir: Path) -> None:
    fixture_dir.mkdir(parents=True, exist_ok=True)
    latest = fetch(f"{host.rstrip('/')}/api/?f=latest&tz=UTC", accept_json=True)
    (fixture_dir / "latest.json").write_text(latest, encoding="utf-8")
    print(f"wrote latest.json ({len(latest)} bytes)")

    show = fetch(f"{host.rstrip('/')}/api/?f=show&tz=UTC&sid=11", accept_json=True)
    (fixture_dir / "show_sid11.json").write_text(show, encoding="utf-8")
    print(f"wrote show_sid11.json ({len(show)} bytes)")

    schedule = fetch(f"{host.rstrip('/')}/api/?f=schedule&tz=UTC", accept_json=True)
    (fixture_dir / "schedule.json").write_text(schedule, encoding="utf-8")
    print(f"wrote schedule.json ({len(schedule)} bytes)")

    shows_html = fetch(f"{host.rstrip('/')}/shows/")
    links = []
    seen = set()
    for m in SHOW_LINK_RE.finditer(shows_html):
        slug = m.group(1)
        if slug in seen:
            continue
        seen.add(slug)
        links.append(slug)
        if len(links) >= 40:
            break
    snippet = '<div class="all-shows">\n' + "\n".join(
        f'<a href="/shows/{s}">show</a>' for s in links
    ) + "\n</div>\n"
    (fixture_dir / "shows_index_snippet.html").write_text(snippet, encoding="utf-8")
    print(f"wrote shows_index_snippet.html ({len(links)} links)")

    show_html = fetch(f"{host.rstrip('/')}/shows/100-man-no-inochi-no-ue-ni-ore-wa-tatte-iru/")
    sid_m = SID_RE.search(show_html)
    sid_html = (
        f'<table id="show-release-table" cellpadding="0" border="0" cellspacing="0" sid="{sid_m.group(1) if sid_m else "11"}"></table>\n'
    )
    (fixture_dir / "show_page_sid11.html").write_text(sid_html, encoding="utf-8")
    print(f"wrote show_page_sid11.html sid={sid_m.group(1) if sid_m else '?'}")


def main() -> int:
    ap = argparse.ArgumentParser(description="Dry-run SubsPlease API parser")
    ap.add_argument("--host", default=DEFAULT_HOST)
    ap.add_argument("--fixture-dir", type=Path, default=DEFAULT_FIXTURE_DIR)
    ap.add_argument("--refresh-fixtures", action="store_true")
    args = ap.parse_args()

    if args.refresh_fixtures:
        try:
            refresh_fixtures(args.host, args.fixture_dir)
        except urllib.error.URLError as exc:
            print(f"refresh failed: {exc}", file=sys.stderr)
            return 1

    latest_path = args.fixture_dir / "latest.json"
    show_path = args.fixture_dir / "show_sid11.json"
    if not latest_path.is_file() or not show_path.is_file():
        print("missing fixtures; run with --refresh-fixtures", file=sys.stderr)
        return 1

    latest = json.loads(latest_path.read_text(encoding="utf-8"))
    kept, dropped = parse_latest(latest)
    print(f"latest: entries={len(latest) if isinstance(latest, dict) else 0} kept1080={len(kept)} dropped={dropped}")
    for row in kept[:5]:
        print(f"  {row['title']} | xl={row['xl']} | {row['page']}")

    show = json.loads(show_path.read_text(encoding="utf-8"))
    sk, sd = parse_show(show, "100-man-no-inochi-no-ue-ni-ore-wa-tatte-iru")
    batches = [r for r in sk if r["batch"]]
    print(f"show sid11: kept1080={len(sk)} dropped={sd} batches={len(batches)}")
    for row in batches:
        print(f"  BATCH {row['episode']} xl={row['xl']} torrent={bool(row.get('torrent'))}")

    # Field coverage on first latest row
    if kept:
        fields = sorted(k for k, v in kept[0].items() if v is not None)
        print("sample fields:", ", ".join(fields))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
