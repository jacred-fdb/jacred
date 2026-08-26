#!/usr/bin/env python3
"""
Dry-run Anifilm listing/detail HTML vs JacRed Go-compatible regexes.

  python3 scripts/dry_run_anifilm_parser.py
  python3 scripts/dry_run_anifilm_parser.py --refresh-fixtures

Optional cookie:
  ANIFILM_COOKIE='...' python3 scripts/dry_run_anifilm_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Anifilm
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

# Keep in sync with AnifilmCategories.Map
CATEGORIES: Dict[str, List[str]] = {
    "serials": ["anime"],
    "ova": ["anime"],
    "ona": ["anime"],
    "movies": ["anime"],
    "dorams": ["serial"],
    "special": ["anime"],
    "hentai": ["anime"],
    "short-serials": ["anime"],
}

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Anifilm"

ITEM_SPLIT_RE = re.compile(r'class="releases__item', re.I)
URL_RE = re.compile(r'<a[^>]+href="/(releases/[^"]+)"', re.I)
NAME_RU_RE = re.compile(r'class="releases__title-russian"[^>]*>([^<]+)</a>', re.I)
NAME_ORIG_RE = re.compile(r'class="releases__title-original"[^>]*>([^<]+)</span>', re.I)
EPISODES_RE = re.compile(r"([0-9]+(-[0-9]+)?)\s*из\s*[0-9]+\s*эп", re.I)
YEAR_RE = re.compile(r'href="/releases/[^"]*">([0-9]{4})</a>', re.I)
YEAR_ALT_RE = re.compile(r"table-list__value[^>]*>[^<]*(\d{4})", re.I)
TID_RE = re.compile(r'href="/(releases/download-torrent/[0-9]+)"[^>]*>скачать</a>', re.I)
CLEAN_SPACE_RE = re.compile(r"[\n\r\t ]+")


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


def _extract(re_obj: re.Pattern[str], row: str) -> str:
    m = re_obj.search(row)
    if not m:
        return ""
    return CLEAN_SPACE_RE.sub(" ", m.group(1)).strip()


def parse_listing(html: str, host: str) -> List[dict]:
    host = host.rstrip("/")
    if "AniFilm" not in html:
        return []
    chunks = ITEM_SPLIT_RE.split(html)
    if len(chunks) < 2:
        return []
    out: List[dict] = []
    for row in chunks[1:]:
        if not row.strip():
            continue
        url_path = _extract(URL_RE, row)
        name = _extract(NAME_RU_RE, row)
        original = _extract(NAME_ORIG_RE, row) or name
        episodes = _extract(EPISODES_RE, row)
        if not url_path or not name:
            continue
        year = _extract(YEAR_RE, row) or _extract(YEAR_ALT_RE, row)
        title = name if original == name else f"{name} / {original}"
        if episodes:
            title += f" ({episodes})"
        out.append(
            {
                "url": f"{host}/{url_path.lstrip('/')}",
                "name": name.split("(")[0].strip() if "(" in name else name,
                "originalname": original,
                "title": title,
                "year": year,
            }
        )
    return out


def extract_tid(detail_html: str) -> Tuple[Optional[str], bool]:
    blocks = detail_html.split('<li class="release__torrents-item">')
    for block in blocks:
        if "1080p" in block and 'href="/releases/download-torrent/' in block:
            m = TID_RE.search(block)
            if m:
                return m.group(1), True
    m = TID_RE.search(detail_html)
    if m:
        return m.group(1), False
    return None, False


def score_listing(html: str, host: str) -> Tuple[int, List[str]]:
    items = parse_listing(html, host)
    samples = [f"{i['title']} | {i['url']}"[:120] for i in items[:3]]
    return len(items), samples


def score_detail(html: str) -> Tuple[bool, Optional[str], bool]:
    tid, is1080 = extract_tid(html)
    return tid is not None, tid, is1080


def write_synthetic_fixtures(fixture_dir: Path) -> None:
    fixture_dir.mkdir(parents=True, exist_ok=True)
    listing = """<!DOCTYPE html>
<html><head><title>AniFilm serials</title></head><body>
<div class="releases__item">
  <a href="/releases/test-show-1">x</a>
  <a class="releases__title-russian" href="/releases/test-show-1">Тестовое аниме (TV)</a>
  <span class="releases__title-original">Test Anime</span>
  12 из 24 эп
  <a href="/releases/test-show-1">2024</a>
</div>
<div class="releases__item">
  <a href="/releases/another-show-2">x</a>
  <a class="releases__title-russian" href="/releases/another-show-2">Другое аниме</a>
  <span class="releases__title-original">Another Anime</span>
  1-3 из 12 эп
  <span class="table-list__value">Год 2023</span>
</div>
</body></html>
"""
    detail = """<!DOCTYPE html>
<html><head><title>AniFilm detail</title></head><body>
<ul class="release__torrents">
<li class="release__torrents-item">
  720p
  <a href="/releases/download-torrent/100">скачать</a>
</li>
<li class="release__torrents-item">
  1080p
  <a href="/releases/download-torrent/101">скачать</a>
</li>
</ul>
</body></html>
"""
    (fixture_dir / "listing_serials.html").write_text(listing, encoding="utf-8")
    (fixture_dir / "detail_sample.html").write_text(detail, encoding="utf-8")
    print(f"wrote synthetic {fixture_dir.relative_to(REPO_ROOT)}/listing_serials.html")
    print(f"wrote synthetic {fixture_dir.relative_to(REPO_ROOT)}/detail_sample.html")


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run Anifilm HTML vs JacRed parser regexes")
    p.add_argument("--host", default=os.environ.get("ANIFILM_HOST", "https://anifilm.pro"))
    p.add_argument("--cookie", default=os.environ.get("ANIFILM_COOKIE", ""))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    p.add_argument("--category", default="serials", choices=list(CATEGORIES))
    args = p.parse_args(argv)

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)
    fixture_dir.mkdir(parents=True, exist_ok=True)

    print("=== Anifilm parser dry-run ===\n")

    report: dict = {"live": False, "listing": None, "detail": None}
    live_ok = False

    list_url = f"{host}/releases/page/1?category={args.category}"
    try:
        list_html = fetch(list_url, args.cookie)
        posts, samples = score_listing(list_html, host)
        valid_list = posts > 0
        status = "OK" if valid_list else "FAIL"
        print(f"[{status}] listing {args.category} posts={posts}")
        for s in samples:
            print(f"         sample: {s}")
        report["listing"] = {"posts": posts, "valid": valid_list, "samples": samples}

        detail_html = ""
        detail_url = ""
        items = parse_listing(list_html, host)
        if items:
            detail_url = items[0]["url"]
            detail_html = fetch(detail_url, args.cookie)
            ok, tid, is1080 = score_detail(detail_html)
            status = "OK" if ok else "FAIL"
            print(f"[{status}] detail tid={tid} 1080p={is1080}")
            report["detail"] = {
                "url": detail_url,
                "tid": tid,
                "is1080p": is1080,
                "valid": ok,
            }
            live_ok = valid_list and ok
        else:
            live_ok = False

        report["live"] = live_ok

        if args.refresh_fixtures and live_ok:
            (fixture_dir / "listing_serials.html").write_text(list_html, encoding="utf-8")
            (fixture_dir / "detail_sample.html").write_text(detail_html, encoding="utf-8")
            print(f"wrote listing_serials.html under {fixture_dir.relative_to(REPO_ROOT)}")
            print(f"wrote detail_sample.html under {fixture_dir.relative_to(REPO_ROOT)}")
        elif args.refresh_fixtures and not live_ok:
            print("[WARN] live fetch incomplete — writing synthetic fixtures")
            write_synthetic_fixtures(fixture_dir)

    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
        print(f"[FAIL] fetch error: {ex}")
        report["error"] = str(ex)
        if args.refresh_fixtures:
            print("[WARN] network failed — writing synthetic fixtures")
            write_synthetic_fixtures(fixture_dir)
        else:
            listing_path = fixture_dir / "listing_serials.html"
            detail_path = fixture_dir / "detail_sample.html"
            if listing_path.exists() and detail_path.exists():
                posts, samples = score_listing(listing_path.read_text(encoding="utf-8"), host)
                ok, tid, is1080 = score_detail(detail_path.read_text(encoding="utf-8"))
                print(f"[FIXTURE] listing posts={posts}")
                print(f"[FIXTURE] detail tid={tid} 1080p={is1080}")
                for s in samples:
                    print(f"         listing: {s}")
                live_ok = posts > 0 and ok
            else:
                return 1

    if args.json_out:
        Path(args.json_out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    if args.refresh_fixtures:
        return 0
    return 0 if (report.get("listing") or {}).get("valid") or live_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
