#!/usr/bin/env python3
"""
Dry-run le-production.online category + detail HTML vs JacRed field shape.

  python3 scripts/dry_run_leproduction_parser.py
  python3 scripts/dry_run_leproduction_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Leproduction
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

# Keep in sync with LeproductionCategories.Map
CATEGORIES: Dict[str, List[str]] = {
    "anime": ["anime"],
    "dorama": ["serial"],
    "film": ["movie"],
    "serial": ["serial"],
    "fulcartoon": ["multfilm"],
    "cartoon": ["multserial"],
}

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Leproduction"

SHORT_IMG_RE = re.compile(
    r'<a\s+class="short-img"\s+href="((?:https?://[^"]+)?/[^"]+?\.html)"', re.I
)
H3_LINK_RE = re.compile(
    r'<h3>\s*<a\s+href="((?:https?://[^"]+)?/[^"]+?\.html)"', re.I
)
DOWNLOAD_ID_RE = re.compile(r"index\.php\?do=download&(?:amp;)?id=(\d+)", re.I)
TORRENT_INFO_RE = re.compile(r'id\s*=\s*"torrent_(\d+)_info"', re.I)
MAGNET_RE = re.compile(r'href\s*=\s*"(magnet:[^"]+)"', re.I)
NAME_RU_RE = re.compile(
    r'Русское\s+название:\s*</div>\s*<div[^>]*class="info-desc"[^>]*>\s*([^<]+)\s*</div>',
    re.I | re.S,
)
SIZE_RE = re.compile(
    r'Размер:\s*<span[^>]*>\s*([0-9]+(?:[.,][0-9]+)?)\s*(Mb|Gb|Tb)\s*</span>', re.I
)


def fetch(url: str) -> str:
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Encoding": "gzip"})
    with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
        raw = resp.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    return raw.decode("utf-8", errors="replace")


def extract_post_urls(html: str, host: str) -> List[str]:
    seen = set()
    out: List[str] = []
    for re_obj in (SHORT_IMG_RE, H3_LINK_RE):
        for m in re_obj.finditer(html):
            u = m.group(1)
            if u.startswith("/"):
                u = host.rstrip("/") + u
            if u not in seen:
                seen.add(u)
                out.append(u)
    return out


def score_listing(html: str, host: str) -> Tuple[int, List[str]]:
    urls = extract_post_urls(html, host)
    return len(urls), urls[:2]


def score_detail(html: str) -> Tuple[bool, List[str], List[str]]:
    name = NAME_RU_RE.search(html)
    ids = DOWNLOAD_ID_RE.findall(html) or TORRENT_INFO_RE.findall(html)
    # unique preserve order
    seen = set()
    uniq_ids: List[str] = []
    for i in ids:
        if i not in seen:
            seen.add(i)
            uniq_ids.append(i)
    magnets = MAGNET_RE.findall(html)
    sizes = SIZE_RE.findall(html)
    ok = bool(name) and len(uniq_ids) > 0 and (len(magnets) > 0 or len(sizes) > 0)
    samples = []
    if name:
        samples.append(re.sub(r"\s+", " ", name.group(1)).strip()[:100])
    return ok, uniq_ids[:3], samples


SYNTHETIC_BROWSE = """<!DOCTYPE html>
<html><body>
<a class="short-img" href="/film/sample-movie-2024.html"><img alt="poster"/></a>
<h3><a href="/film/sample-movie-2024.html">Sample Movie</a></h3>
<a class="short-img" href="/film/other-title-2023.html"><img alt="poster"/></a>
<h3><a href="/film/other-title-2023.html">Other Title</a></h3>
</body></html>
"""

SYNTHETIC_DETAIL = """<!DOCTYPE html>
<html><body>
<h1>Тестовый фильм / Sample Movie</h1>
<div class="info-label">Русское название:</div>
<div class="info-desc">Тестовый фильм</div>
<div class="info-label">Оригинальное название:</div>
<div class="info-desc">Sample Movie</div>
<div class="info-label">Год выпуска:</div>
<div class="info-desc"><a href="/xfsearch/2024/">2024</a></div>
<div id="torrent_12345_info">
  <div class="info_d1-le">Sample.Movie.2024.1080p.WEB-DL [1 из 1]</div>
  <b>Раздают:</b> <span class="li_distribute_m-le">12</span>
  <b>Качают:</b> <span class="li_swing_m-le">3</span>
  Размер: <span>1.50 Gb</span>
  <a href="magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567&amp;dn=Sample">magnet</a>
  <a href="/index.php?do=download&amp;id=12345">download</a>
</div>
<div id="torrent_67890_info">
  <div class="info_d1-le">Sample.Movie.2024.720p.WEB-DL</div>
  <b>Раздают:</b> <span class="li_distribute_m-le">5</span>
  <b>Качают:</b> <span class="li_swing_m-le">1</span>
  Размер: <span>800 Mb</span>
  <a href="magnet:?xt=urn:btih:FEDCBA9876543210FEDCBA9876543210FEDCBA98&amp;dn=Sample720">magnet</a>
  <a href="/index.php?do=download&amp;id=67890">download</a>
</div>
</body></html>
"""


def write_synthetic(fixture_dir: Path) -> None:
    fixture_dir.mkdir(parents=True, exist_ok=True)
    for cat in CATEGORIES:
        (fixture_dir / f"browse_{cat}.html").write_text(SYNTHETIC_BROWSE, encoding="utf-8")
    (fixture_dir / "detail_sample.html").write_text(SYNTHETIC_DETAIL, encoding="utf-8")
    print(f"wrote synthetic fixtures under {fixture_dir.relative_to(REPO_ROOT)}")


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run le-production.online HTML vs JacRed")
    p.add_argument("--host", default=os.environ.get("LEPRODUCTION_HOST", "https://www.le-production.online"))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    p.add_argument("--synthetic", action="store_true", help="Write synthetic fixtures only")
    args = p.parse_args(argv)

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)

    if args.synthetic:
        write_synthetic(fixture_dir)
        return 0

    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        for stale in fixture_dir.glob("browse_*.html"):
            stale.unlink()
        detail = fixture_dir / "detail_sample.html"
        if detail.exists():
            detail.unlink()

    report = []
    failed = False
    live_ok = False
    detail_html: Optional[str] = None
    print(f"=== le-production parser dry-run ({len(CATEGORIES)} categories) ===\n")

    for cat, types in CATEGORIES.items():
        url = f"{host}/{cat}/"
        try:
            html = fetch(url)
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
            print(f"[FAIL] cat={cat:<12} fetch error: {ex}")
            failed = True
            report.append({"cat": cat, "types": types, "valid": False, "error": str(ex)})
            continue

        posts, samples = score_listing(html, host)
        valid = posts > 0
        if not valid:
            failed = True
        else:
            live_ok = True

        status = "OK" if valid else "FAIL"
        print(f"[{status}] cat={cat:<12} types={types} posts={posts}")
        for s in samples:
            print(f"         sample: {s}")

        if args.refresh_fixtures and valid:
            out = fixture_dir / f"browse_{cat}.html"
            out.write_text(html, encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")

            if detail_html is None and samples:
                try:
                    detail_html = fetch(samples[0])
                    dok, ids, dsamples = score_detail(detail_html)
                    print(
                        f"         detail ok={dok} ids={ids} "
                        f"samples={dsamples}"
                    )
                    if dok:
                        dout = fixture_dir / "detail_sample.html"
                        dout.write_text(detail_html, encoding="utf-8")
                        print(f"         wrote {dout.relative_to(REPO_ROOT)}")
                    else:
                        detail_html = None
                except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
                    print(f"         detail fetch error: {ex}")
                    detail_html = None

        report.append(
            {
                "cat": cat,
                "types": types,
                "posts": posts,
                "valid": valid,
                "samples": samples,
            }
        )

    if args.refresh_fixtures and (failed or not live_ok or detail_html is None):
        print("\nLive fetch incomplete — seeding synthetic fixtures for missing files.")
        for cat in CATEGORIES:
            path = fixture_dir / f"browse_{cat}.html"
            if not path.exists():
                path.write_text(SYNTHETIC_BROWSE, encoding="utf-8")
                print(f"         wrote synthetic {path.relative_to(REPO_ROOT)}")
        detail = fixture_dir / "detail_sample.html"
        if not detail.exists():
            detail.write_text(SYNTHETIC_DETAIL, encoding="utf-8")
            print(f"         wrote synthetic {detail.relative_to(REPO_ROOT)}")

    if args.json_out:
        Path(args.json_out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    if failed and not args.refresh_fixtures:
        print("Dry-run FAILED.", file=sys.stderr)
        return 2

    print("Dry-run done. Run:")
    print("  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Leproduction")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
