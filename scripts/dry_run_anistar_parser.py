#!/usr/bin/env python3
"""
Dry-run Anistar listing/detail HTML vs JacRed Go-compatible regexes.

  python3 scripts/dry_run_anistar_parser.py
  python3 scripts/dry_run_anistar_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Anistar
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
from typing import List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Anistar"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

CATEGORIES = ("anime", "hentai", "dorams")

POST_URL_ABS_RE = re.compile(r'https?://[^"\'>]+/\d{2,}-[^"\'>]+?\.html')
POST_URL_REL_RE = re.compile(r'/\d{2,}-[^"\'>]+?\.html')
TORRENT_BLOCK_RE = re.compile(r'<div id="torrent_(\d+)_info"\s+class="torrent"', re.I)
H1_RE = re.compile(r"<h1[^>]*>\s*(.*?)\s*</h1>", re.I | re.S)
INFO_D1_RE = re.compile(r'<div class="info_d1">\s*([^<]+?)\s*</div>', re.I | re.S)
DATE_RE = re.compile(r"\b(\d{2})-(\d{2})-(\d{4})\b")
SID_RE = re.compile(r'<div class="li_distribute">\s*([0-9]+)\s*</div>', re.I)
PIR_RE = re.compile(r'<div class="li_swing">\s*([0-9]+)\s*</div>', re.I)


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
    try:
        return raw.decode("cp1251")
    except UnicodeDecodeError:
        return raw.decode("utf-8", errors="replace")


def extract_post_urls(html: str, host: str) -> List[str]:
    host = host.rstrip("/")
    seen = set()
    out: List[str] = []
    for m in POST_URL_ABS_RE.findall(html):
        if m not in seen:
            seen.add(m)
            out.append(m)
    for m in POST_URL_REL_RE.findall(html):
        abs_url = host + m
        if abs_url not in seen:
            seen.add(abs_url)
            out.append(abs_url)
    return out


def score_listing(html: str, host: str, sample_limit: int = 8) -> Tuple[int, List[str]]:
    urls = extract_post_urls(html, host)
    return len(urls), urls[:sample_limit]


def score_detail(html: str) -> Tuple[int, int, List[str]]:
    blocks = list(TORRENT_BLOCK_RE.finditer(html))
    ok = 0
    samples: List[str] = []
    h1_m = H1_RE.search(html)
    h1 = re.sub(r"\s+", " ", h1_m.group(1)).strip() if h1_m else ""
    for bm in blocks:
        start = bm.start()
        around = html[start : start + 4000]
        has_info = bool(INFO_D1_RE.search(around) or DATE_RE.search(around))
        has_peers = bool(SID_RE.search(around) or PIR_RE.search(around))
        if has_info or has_peers:
            ok += 1
            if len(samples) < 2:
                info = INFO_D1_RE.search(around)
                label = info.group(1).strip() if info else bm.group(1)
                samples.append(re.sub(r"\s+", " ", f"{h1} | {label}")[:120])
    return len(blocks), ok, samples


def write_synthetic_fixtures(fixture_dir: Path) -> None:
    fixture_dir.mkdir(parents=True, exist_ok=True)
    listing = """<!DOCTYPE html>
<html><head><title>Anistar anime</title></head><body>
<a href="https://v30.astar.bz/12-test-show.html">Test Show</a>
<a href="/34-another-anime.html">Another</a>
<div class="navigation"><a href="/anime/page/2/">2</a><a href="/anime/page/5/">5</a></div>
</body></html>
"""
    detail = """<!DOCTYPE html>
<html><head><title>detail</title></head><body>
<h1>Тестовое аниме / Test Anime</h1>
<div id="torrent_1001_info" class="torrent">
  <div class="info_d1">Серия 3</div>
  <div>12-07-2024</div>
  <div class="li_distribute">10</div>
  <div class="li_swing">2</div>
</div>
<div id="torrent_1002_info" class="torrent">
  <div class="info_d1">Серии 1-12</div>
  <div>01-01-2023</div>
  <div class="li_distribute">5</div>
  <div class="li_swing">1</div>
</div>
</body></html>
"""
    (fixture_dir / "listing_anime.html").write_text(listing, encoding="utf-8")
    (fixture_dir / "detail_sample.html").write_text(detail, encoding="utf-8")
    print(f"wrote synthetic {fixture_dir.relative_to(REPO_ROOT)}/listing_anime.html")
    print(f"wrote synthetic {fixture_dir.relative_to(REPO_ROOT)}/detail_sample.html")


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run Anistar HTML vs JacRed parser regexes")
    p.add_argument("--host", default=os.environ.get("ANISTAR_HOST", "https://v30.astar.bz"))
    p.add_argument("--cookie", default=os.environ.get("ANISTAR_COOKIE", ""))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    p.add_argument("--category", default="anime", choices=list(CATEGORIES))
    args = p.parse_args(argv)

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)
    fixture_dir.mkdir(parents=True, exist_ok=True)

    print(f"=== Anistar parser dry-run ===\n")

    report = {"live": False, "listing": None, "detail": None}
    live_ok = False

    list_url = f"{host}/{args.category}/"
    try:
        list_html = fetch(list_url, args.cookie)
        posts, samples = score_listing(list_html, host)
        valid_list = posts > 0
        status = "OK" if valid_list else "FAIL"
        print(f"[{status}] listing {args.category} posts={posts}")
        for s in samples[:3]:
            print(f"         sample: {s}")
        report["listing"] = {"posts": posts, "valid": valid_list, "samples": samples[:3]}

        detail_html = ""
        detail_url = ""
        blocks = ok = 0
        detail_samples: List[str] = []
        valid_detail = False
        for candidate in samples:
            candidate_html = fetch(candidate, args.cookie)
            candidate_blocks, candidate_ok, candidate_samples = score_detail(candidate_html)
            if candidate_blocks <= 0:
                print(f"[SKIP] detail {candidate} blocks=0 (promo/no torrents)")
                continue
            detail_url = candidate
            detail_html = candidate_html
            blocks, ok, detail_samples = candidate_blocks, candidate_ok, candidate_samples
            valid_detail = True
            break

        rate = round(ok / blocks * 100, 1) if blocks else 0.0
        status = "OK" if valid_detail else "FAIL"
        print(f"[{status}] detail blocks={blocks} ok={ok} rate={rate}%")
        if detail_url:
            print(f"         url: {detail_url}")
        for s in detail_samples:
            print(f"         sample: {s}")
        report["detail"] = {
            "url": detail_url,
            "blocks": blocks,
            "ok": ok,
            "rate": rate,
            "valid": valid_detail,
            "samples": detail_samples,
        }
        live_ok = valid_list and valid_detail

        report["live"] = live_ok

        if args.refresh_fixtures and live_ok:
            (fixture_dir / "listing_anime.html").write_text(list_html, encoding="utf-8")
            (fixture_dir / "detail_sample.html").write_text(detail_html, encoding="utf-8")
            print(f"wrote {Path('listing_anime.html')} under {fixture_dir.relative_to(REPO_ROOT)}")
            print(f"wrote {Path('detail_sample.html')} under {fixture_dir.relative_to(REPO_ROOT)}")
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
            # Score existing fixtures if present
            listing_path = fixture_dir / "listing_anime.html"
            detail_path = fixture_dir / "detail_sample.html"
            if listing_path.exists() and detail_path.exists():
                posts, samples = score_listing(listing_path.read_text(encoding="utf-8"), host)
                blocks, ok, detail_samples = score_detail(detail_path.read_text(encoding="utf-8"))
                print(f"[FIXTURE] listing posts={posts}")
                print(f"[FIXTURE] detail blocks={blocks} ok={ok}")
                for s in samples:
                    print(f"         listing: {s}")
                for s in detail_samples:
                    print(f"         detail: {s}")
                live_ok = posts > 0 and blocks > 0
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
