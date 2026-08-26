#!/usr/bin/env python3
"""
Dry-run viruseproject.tv category + detail HTML vs JacRed field shape.

  python3 scripts/dry_run_viruseproject_parser.py
  python3 scripts/dry_run_viruseproject_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Viruseproject
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

# Keep in sync with ViruseprojectCategories.Map
CATEGORIES: Dict[str, List[str]] = {
    "serials": ["serial"],
    "movies": ["movie"],
    "documentary": ["docuserial", "documovie"],
    "cartoons": ["multfilm", "multserial"],
    "reality-show": ["tvshow"],
}

CAT_PAGE_STEP: Dict[str, int] = {
    "serials": 10,
    "movies": 10,
    "documentary": 6,
    "cartoons": 6,
    "reality-show": 6,
}

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Viruseproject"

ITEM_HREF_RE = re.compile(
    r'<h3\s+class="catItemTitle">\s*<a\s+href="([^"]+)"', re.I | re.S
)
PAGINATION_END_RE = re.compile(
    r'<li\s+class="pagination-end">\s*<a[^>]+href="[^"]*?[?&]start=(\d+)"', re.I | re.S
)
ITEM_TITLE_RE = re.compile(r'<h2\s+class="itemTitle">\s*(.+?)\s*</h2>', re.I | re.S)
ATTACHMENT_RE = re.compile(
    r'<a\s+title="([^"]+?\.torrent)"\s+href="([^"]+/download/(\d+)_[a-f0-9]+)"\s*>\s*([^<]+?)\s*</a>',
    re.I | re.S,
)
EXTRA_FIELD_RE = re.compile(
    r'<span\s+class="itemExtraFieldsLabel">\s*([^<]+?)\s*</span>\s*'
    r'<span\s+class="itemExtraFieldsValue">\s*([^<]+?)\s*</span>',
    re.I | re.S,
)
STRIP_TAGS_RE = re.compile(r"<[^>]+>")


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


def clean_text(s: str) -> str:
    s = STRIP_TAGS_RE.sub("", s or "")
    return re.sub(r"\s+", " ", s).strip()


def extract_post_urls(html: str, host: str) -> List[str]:
    host = host.rstrip("/")
    seen = set()
    out: List[str] = []
    for m in ITEM_HREF_RE.finditer(html):
        u = m.group(1).strip()
        if u.startswith("/"):
            u = host + u
        if u not in seen:
            seen.add(u)
            out.append(u)
    return out


def detect_last_page(html: str, step: int) -> int:
    if step <= 0:
        return 1
    m = PAGINATION_END_RE.search(html)
    if m:
        last = int(m.group(1))
        if last > 0:
            return last // step + 1
    return 1


def score_detail(html: str) -> Tuple[bool, int, List[str]]:
    title_m = ITEM_TITLE_RE.search(html)
    atts = ATTACHMENT_RE.findall(html)
    fields = {clean_text(l).rstrip(":"): clean_text(v) for l, v in EXTRA_FIELD_RE.findall(html)}
    ok = bool(title_m) and len(atts) > 0 and "Год выпуска" in fields
    samples: List[str] = []
    if title_m:
        samples.append(clean_text(title_m.group(1))[:100])
    for att in atts[:2]:
        samples.append(att[0][:80])
    return ok, len(atts), samples


SYNTHETIC_BROWSE = """<!DOCTYPE html>
<html><body>
<div class="itemList">
  <h3 class="catItemTitle">
    <a href="/releases/movies/sample-movie-2024">Sample Movie</a>
  </h3>
  <h3 class="catItemTitle">
    <a href="/releases/movies/other-title-2023">Other Title</a>
  </h3>
</div>
<ul class="pagination">
  <li class="pagination-end">
    <a title="В конец" href="/releases/movies?start=20">Конец</a>
  </li>
</ul>
</body></html>
"""

SYNTHETIC_DETAIL = """<!DOCTYPE html>
<html><body>
<h2 class="itemTitle">Шугар / Sugar / сезон 2</h2>
<span class="itemDateCreated">Суббота, 08 Август 2026 00:00</span>
<span class="itemExtraFieldsLabel">Год выпуска:</span>
<span class="itemExtraFieldsValue">2026</span>
<span class="itemExtraFieldsLabel">Качество видео:</span>
<span class="itemExtraFieldsValue">WEBRip</span>
<a title="S.2024.S02.1080p.VP.torrent"
   href="/releases/serials/download/13548_282caa7034e8d53102b96593fc83fbe9">
  S.2024.S02.1080p.VP (размер 9,87 Гб)
</a>
<a title="S.2024.S02.400p.VP.torrent"
   href="/releases/serials/download/13549_8d3fb4c7f6209b1ce17a2bc4910a969e">
  S.2024.S02.400p.VP (размер 3,49 Гб)
</a>
</body></html>
"""


def write_synthetic(fixture_dir: Path) -> None:
    fixture_dir.mkdir(parents=True, exist_ok=True)
    for cat in CATEGORIES:
        (fixture_dir / f"browse_{cat}.html").write_text(SYNTHETIC_BROWSE, encoding="utf-8")
    (fixture_dir / "detail_sample.html").write_text(SYNTHETIC_DETAIL, encoding="utf-8")
    print(f"wrote synthetic fixtures under {fixture_dir.relative_to(REPO_ROOT)}")


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run viruseproject.tv HTML vs JacRed")
    p.add_argument("--host", default=os.environ.get("VIRUSEPROJECT_HOST", "https://viruseproject.tv"))
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
    print(f"=== viruseproject parser dry-run ({len(CATEGORIES)} categories) ===\n")

    for cat, types in CATEGORIES.items():
        url = f"{host}/releases/{cat}?start=0"
        try:
            html = fetch(url)
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
            print(f"[FAIL] cat={cat:<14} fetch error: {ex}")
            failed = True
            report.append({"cat": cat, "types": types, "valid": False, "error": str(ex)})
            continue

        posts = extract_post_urls(html, host)
        step = CAT_PAGE_STEP.get(cat, 10)
        last_page = detect_last_page(html, step)
        valid = len(posts) > 0
        if not valid:
            failed = True
        else:
            live_ok = True

        status = "OK" if valid else "FAIL"
        print(
            f"[{status}] cat={cat:<14} types={types} posts={len(posts)} "
            f"last_page={last_page} step={step}"
        )
        for s in posts[:2]:
            print(f"         sample: {s}")

        if args.refresh_fixtures and valid:
            out = fixture_dir / f"browse_{cat}.html"
            out.write_text(html, encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")

            if detail_html is None and posts:
                # Prefer a post with multiple .torrent attachments.
                best = None
                best_n = 0
                for post in posts[:5]:
                    try:
                        dhtml = fetch(post)
                    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
                        print(f"         detail fetch error: {ex}")
                        continue
                    dok, n, dsamples = score_detail(dhtml)
                    print(f"         detail ok={dok} atts={n} samples={dsamples}")
                    if dok and n > best_n:
                        best_n = n
                        best = dhtml
                if best is not None:
                    detail_html = best
                    dout = fixture_dir / "detail_sample.html"
                    dout.write_text(detail_html, encoding="utf-8")
                    print(f"         wrote {dout.relative_to(REPO_ROOT)}")

        report.append(
            {
                "cat": cat,
                "types": types,
                "posts": len(posts),
                "last_page": last_page,
                "valid": valid,
                "samples": posts[:2],
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
    print("  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Viruseproject")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
