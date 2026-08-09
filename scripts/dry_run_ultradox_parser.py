#!/usr/bin/env python3
"""
Dry-run ultradox.onl listing/detail HTML vs JacRed Go-compatible regexes.

CRITICAL: Referer must look like google/yandex search — own origin returns 503.

Fixtures were seeded from temp/jacred-go/cron/ultradox/testdata/.

  python3 scripts/dry_run_ultradox_parser.py
  python3 scripts/dry_run_ultradox_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Ultradox
"""

from __future__ import annotations

import argparse
import gzip
import io
import json
import os
import re
import shutil
import ssl
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Dict, List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Ultradox"
GO_TESTDATA = REPO_ROOT / "temp" / "jacred-go" / "cron" / "ultradox" / "testdata"

# Keep in sync with UltradoxCategories.Map
SECTIONS: Tuple[Tuple[str, List[str]], ...] = (
    ("serial-hd", ["serial"]),
    ("hd", ["movie"]),
    ("rufilm", ["movie"]),
    ("camrip", ["movie"]),
    ("webrips", ["movie"]),
    ("anime", ["anime"]),
)

UA = (
    "Mozilla/5.0 (X11; Linux x86_64; rv:153.0) "
    "Gecko/20100101 Firefox/153.0"
)

# Site nginx 503s without a search-engine Referer.
GOOGLE_REFERER = "https://www.google.com/"

ROW_SPLIT_RE = re.compile(r'<tr>\s*<td class="torrent-table-date">')
ROW_DATE_RE = re.compile(r"^([^<]+)</td>")
ROW_DETAIL_LINK_RE = re.compile(
    r'<td class="torrent-table-href">\s*<a[^>]+href="([^"#]+)"[^>]*>([\s\S]*?)</a>',
    re.I | re.S,
)
ROW_IMDB_RE = re.compile(
    r'<span\s+data-clipboard-text="https://www\.imdb\.com/title/(tt[0-9]+)/?"',
    re.I | re.S,
)
ROW_SPAN_QUALITY_RE = re.compile(
    r'<span[^>]*style="font-weight:\s*bold;?"[^>]*>([\s\S]*?)</span>',
    re.I | re.S,
)
ROW_TAGS_RE = re.compile(r"<[^>]+>")
DETAIL_MAGNET_RE = re.compile(
    r"magnet:\?xt=urn:btih:([A-Fa-f0-9]+)&xl=([0-9]+)&dn=([^&\"<\s]+)",
    re.I,
)
DETAIL_YEAR_RE = re.compile(
    r'itemprop="copyrightYear"[^>]*>\s*<span>[^<]*</span>\s*([0-9]{4})',
    re.I | re.S,
)
PAGE_NUM_RE = re.compile(r"/page/([0-9]+)/")
QUALITY_RES_RE = re.compile(r"([0-9]{3,4})[pP]")
STRIP_TAGS_RE = re.compile(r"<[^>]+>")
SPACES_RE = re.compile(r"\s+")


def fetch(url: str) -> str:
    """GET with google Referer — own-origin Referer returns 503."""
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    headers = {
        "User-Agent": UA,
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7",
        "Cache-Control": "no-cache",
        "Pragma": "no-cache",
        "Referer": GOOGLE_REFERER,
        "Sec-Fetch-Dest": "document",
        "Sec-Fetch-Mode": "navigate",
        "Sec-Fetch-Site": "cross-site",
        "Sec-Fetch-User": "?1",
        "Upgrade-Insecure-Requests": "1",
        "Accept-Encoding": "gzip",
    }
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
        raw = resp.read()
        final = resp.geturl()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    body = raw.decode("utf-8", errors="replace")
    if "503" in body[:200] and "torrent-table" not in body:
        raise urllib.error.HTTPError(url, 503, "referer gate / empty body", hdrs=None, fp=None)
    if final and final != url:
        print(f"         redirected → {final}")
    return body


def clean_text(s: str) -> str:
    s = STRIP_TAGS_RE.sub(" ", s or "")
    s = (
        s.replace("&amp;", "&")
        .replace("&quot;", '"')
        .replace("&#39;", "'")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&nbsp;", " ")
    )
    return SPACES_RE.sub(" ", s).strip()


def flatten_title(raw: str) -> str:
    span = ""
    m = ROW_SPAN_QUALITY_RE.search(raw or "")
    if m:
        span = m.group(1)
    main = ROW_TAGS_RE.sub(" ", raw or "")
    main = (
        main.replace("&amp;", "&")
        .replace("&quot;", '"')
        .replace("&#39;", "'")
        .replace("&nbsp;", " ")
    )
    if span:
        span_plain = clean_text(span)
        main = main.replace(span_plain, "")
        main = main.strip() + " " + span_plain
    return clean_text(main)


def parse_listing(html: str) -> List[Dict[str, str]]:
    chunks = ROW_SPLIT_RE.split(html or "")
    out: List[Dict[str, str]] = []
    for row in chunks[1:]:
        row = row.strip()
        if not row:
            continue
        dm = ROW_DATE_RE.match(row)
        if not dm:
            continue
        lm = ROW_DETAIL_LINK_RE.search(row)
        if not lm:
            continue
        title = flatten_title(lm.group(2))
        if not title:
            continue
        imdb = ""
        im = ROW_IMDB_RE.search(row)
        if im:
            imdb = im.group(1)
        out.append(
            {
                "date": dm.group(1).strip(),
                "detail": lm.group(1).strip(),
                "title": title,
                "imdb": imdb,
            }
        )
    return out


def extract_quality(dn: str) -> str:
    clean = (dn or "").replace("O", "0")
    m = QUALITY_RES_RE.search(clean)
    if m:
        return m.group(1) + "p"
    for tag in ("BDRip", "DVDRip", "HDRip", "WEBRip", "WEB-DL", "CAMRip", "CamRip", "TS"):
        if tag in (dn or ""):
            return tag
    return ""


def score_detail(html: str) -> Tuple[bool, int, List[str]]:
    year = 0
    ym = DETAIL_YEAR_RE.search(html or "")
    if ym:
        year = int(ym.group(1))
    magnets = DETAIL_MAGNET_RE.findall(html or "")
    samples: List[str] = []
    seen = set()
    for hash_, xl, dn in magnets:
        h = hash_.lower()
        if h in seen:
            continue
        seen.add(h)
        q = extract_quality(dn)
        samples.append(f"{q or '?'} {h[:8]}… xl={xl}")
    return len(magnets) > 0, year, samples


def last_page(html: str) -> int:
    max_page = 1
    for m in PAGE_NUM_RE.finditer(html or ""):
        n = int(m.group(1))
        if n > max_page:
            max_page = n
    return max_page


def seed_from_go(fixture_dir: Path) -> bool:
    if not GO_TESTDATA.is_dir():
        return False
    fixture_dir.mkdir(parents=True, exist_ok=True)
    copied = 0
    for name in ("listing_serial-hd.html", "detail_serial.html"):
        src = GO_TESTDATA / name
        if src.is_file():
            shutil.copy2(src, fixture_dir / name)
            copied += 1
            print(f"         seeded {name} from Go testdata")
    return copied > 0


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run ultradox.onl HTML vs JacRed (google Referer)")
    p.add_argument("--host", default=os.environ.get("ULTRADOX_HOST", "https://ultradox.onl"))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    p.add_argument("--seed-go", action="store_true", help="Copy fixtures from Go testdata only")
    args = p.parse_args(argv)

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)

    if args.seed_go:
        ok = seed_from_go(fixture_dir)
        return 0 if ok else 2

    report = []
    failed = False
    print(f"=== ultradox parser dry-run ({len(SECTIONS)} sections, Referer={GOOGLE_REFERER}) ===\n")

    listing_fix = fixture_dir / "listing_serial-hd.html"
    if listing_fix.is_file():
        html = listing_fix.read_text(encoding="utf-8", errors="replace")
        items = parse_listing(html)
        placeholders = "magnet:?xt=urn:btih:&" in html
        ok = len(items) == 18 and placeholders
        status = "OK" if ok else "FAIL"
        if not ok:
            failed = True
        print(
            f"[{status}] fixture listing_serial-hd.html rows={len(items)} "
            f"placeholder_magnets={placeholders}"
        )
        for it in items[:2]:
            print(f"         {it['detail']}  {it['title'][:90]}")
        report.append(
            {
                "fixture": "listing_serial-hd.html",
                "rows": len(items),
                "placeholder_magnets": placeholders,
                "valid": ok,
            }
        )
    else:
        print("[WARN] fixture listing_serial-hd.html missing — run with --seed-go or --refresh-fixtures")

    detail_fix = fixture_dir / "detail_serial.html"
    if detail_fix.is_file():
        html = detail_fix.read_text(encoding="utf-8", errors="replace")
        ok, year, samples = score_detail(html)
        status = "OK" if ok and year == 2026 and len(samples) == 3 else "FAIL"
        if status == "FAIL":
            failed = True
        print(f"[{status}] fixture detail_serial.html year={year} variants={len(samples)}")
        for s in samples:
            print(f"         {s}")
        report.append(
            {
                "fixture": "detail_serial.html",
                "year": year,
                "variants": len(samples),
                "valid": status == "OK",
                "samples": samples,
            }
        )
    else:
        print("[WARN] fixture detail_serial.html missing")

    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        live_ok = False
        detail_written = False
        for path, types in SECTIONS:
            url = f"{host}/{path}/"
            try:
                html = fetch(url)
            except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
                print(f"[FAIL] {path} fetch error: {ex}")
                failed = True
                report.append({"section": path, "types": types, "valid": False, "error": str(ex)})
                continue

            items = parse_listing(html)
            lp = last_page(html)
            valid = len(items) > 0
            if valid:
                live_ok = True
            status = "OK" if valid else "FAIL"
            if not valid:
                failed = True
            print(f"[{status}] {path} rows={len(items)} last_page={lp} types={types}")
            for it in items[:2]:
                print(f"         {it['detail']}  {it['title'][:90]}")

            out = fixture_dir / f"listing_{path}.html"
            out.write_text(html, encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")

            if not detail_written and items:
                for it in items[:3]:
                    dpath = it["detail"]
                    durl = dpath if dpath.startswith("http") else f"{host}/{dpath.lstrip('/')}"
                    try:
                        dhtml = fetch(durl)
                    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
                        print(f"         detail fetch error: {ex}")
                        continue
                    dok, dyear, dsamples = score_detail(dhtml)
                    print(f"         detail ok={dok} year={dyear} {dsamples}")
                    if dok:
                        dout = fixture_dir / "detail_sample.html"
                        dout.write_text(dhtml, encoding="utf-8")
                        # Keep Go-compatible name when refreshing serial-hd.
                        if path == "serial-hd":
                            (fixture_dir / "detail_serial.html").write_text(dhtml, encoding="utf-8")
                        print(f"         wrote {dout.relative_to(REPO_ROOT)}")
                        detail_written = True
                        break

            report.append(
                {
                    "section": path,
                    "types": types,
                    "rows": len(items),
                    "last_page": lp,
                    "valid": valid,
                }
            )

        if not live_ok:
            print("\nLive fetch incomplete — seeding from Go testdata.")
            seed_from_go(fixture_dir)

    if args.json_out:
        Path(args.json_out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    if failed and not args.refresh_fixtures:
        print("Dry-run FAILED.", file=sys.stderr)
        return 2

    print("Dry-run done. Run:")
    print("  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Ultradox")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
