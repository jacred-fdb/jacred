#!/usr/bin/env python3
"""
Dry-run Anibelka listing/topic HTML vs JacRed Go-compatible regexes.

Fixtures were seeded from temp/jacred-go/cron/anibelka/testdata/.

  python3 scripts/dry_run_anibelka_parser.py
  python3 scripts/dry_run_anibelka_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Anibelka
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
from typing import List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Anibelka"
GO_TESTDATA = REPO_ROOT / "temp" / "jacred-go" / "cron" / "anibelka" / "testdata"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

# Keep in sync with AnibelkaCategories.Map
SECTIONS = (
    ("32", "Универсальные"),
    ("33", "С озвучкой"),
    ("34", "С субтитрами"),
    ("36", "Полнометражки"),
    ("37", "PSP"),
)

TOPICS_PER_PAGE = 15

ROW_TOPIC_RE = re.compile(
    r'href="\./viewtopic\.php\?t=(\d+)[^"]*"\s+class="topictitle">(.*?)</a>',
    re.I | re.S,
)
PAGE_START_RE = re.compile(r"start=(\d+)")
TORRENT_LINK_RE = re.compile(
    r'href="\./download/file\.php\?id=(\d+)[^"]*"[^>]*tooltip="Скачать торрент"',
    re.I | re.S,
)
SIZE_RE = re.compile(r"Размер:\s*<b>([0-9.,]+)&nbsp;(КБ|МБ|ГБ|ТБ)</b>", re.I | re.S)
SEED_RE = re.compile(r'Сидеров:\s*<span class="seed">\s*<b>(\d+)</b>', re.I | re.S)
LEECH_RE = re.compile(r'Личеров:\s*<span class="leech">\s*<b>(\d+)</b>', re.I | re.S)
ADDED_RE = re.compile(r"Добавлен:\s*<b>\s*<span[^>]*>([^<]+)</span>", re.I | re.S)
STRIP_TAGS_RE = re.compile(r"<[^>]+>")
SPACES_RE = re.compile(r"\s+")


def fetch(url: str) -> str:
    """Anonymous fetch — never send cookies (passkey risk)."""
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    headers = {"User-Agent": UA, "Accept-Encoding": "gzip"}
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, context=ctx, timeout=45) as resp:
        raw = resp.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    return raw.decode("utf-8", errors="replace")


def clean_text(s: str) -> str:
    s = STRIP_TAGS_RE.sub("", s)
    s = (
        s.replace("&amp;", "&")
        .replace("&quot;", '"')
        .replace("&#39;", "'")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&nbsp;", " ")
    )
    return SPACES_RE.sub(" ", s).strip()


def parse_listing(html: str) -> List[Tuple[str, str]]:
    out: List[Tuple[str, str]] = []
    seen = set()
    for m in ROW_TOPIC_RE.finditer(html):
        tid, title = m.group(1), clean_text(m.group(2))
        if not title.startswith("[") or tid in seen:
            continue
        seen.add(tid)
        out.append((tid, title))
    return out


def last_page(html: str) -> int:
    max_start = 0
    for m in PAGE_START_RE.finditer(html):
        n = int(m.group(1))
        if n > max_start:
            max_start = n
    return max_start // TOPICS_PER_PAGE


def score_topic(html: str) -> Tuple[bool, str, List[str]]:
    m = TORRENT_LINK_RE.search(html)
    if not m:
        return False, "", []
    tid = m.group(1)
    samples = [f"torrent_id={tid}"]
    sm = SIZE_RE.search(html)
    if sm:
        samples.append(f"{sm.group(1)} {sm.group(2)}")
    if SEED_RE.search(html):
        samples.append(f"sid={SEED_RE.search(html).group(1)}")
    if ADDED_RE.search(html):
        samples.append(f"added={clean_text(ADDED_RE.search(html).group(1))}")
    return True, tid, samples


def seed_from_go(fixture_dir: Path) -> bool:
    if not GO_TESTDATA.is_dir():
        return False
    fixture_dir.mkdir(parents=True, exist_ok=True)
    copied = 0
    for name in ("forum_f33.html", "topic_rus.html", "topic_mv.html"):
        src = GO_TESTDATA / name
        if src.is_file():
            shutil.copy2(src, fixture_dir / name)
            copied += 1
            print(f"         seeded {name} from Go testdata")
    return copied > 0


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run anibelka.com HTML vs JacRed (anonymous)")
    p.add_argument("--host", default=os.environ.get("ANIBELKA_HOST", "https://anibelka.com"))
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
    print(f"=== anibelka parser dry-run ({len(SECTIONS)} sections, anonymous) ===\n")

    # Score saved forum fixture first (Go-seeded).
    forum_fix = fixture_dir / "forum_f33.html"
    if forum_fix.is_file():
        html = forum_fix.read_text(encoding="utf-8", errors="replace")
        items = parse_listing(html)
        lp = last_page(html)
        ok = len(items) > 0 and lp == 40
        status = "OK" if ok else "FAIL"
        if not ok:
            failed = True
        print(f"[{status}] fixture forum_f33.html topics={len(items)} last_page={lp}")
        for tid, title in items[:2]:
            print(f"         t={tid} {title[:90]}")
        report.append({"fixture": "forum_f33.html", "topics": len(items), "last_page": lp, "valid": ok})
    else:
        print("[WARN] fixture forum_f33.html missing — run with --seed-go or --refresh-fixtures")

    for name in ("topic_rus.html", "topic_mv.html"):
        path = fixture_dir / name
        if not path.is_file():
            print(f"[WARN] fixture {name} missing")
            continue
        html = path.read_text(encoding="utf-8", errors="replace")
        ok, tid, samples = score_topic(html)
        status = "OK" if ok else "FAIL"
        if not ok:
            failed = True
        print(f"[{status}] fixture {name} torrent_id={tid} samples={samples}")
        report.append({"fixture": name, "torrent_id": tid, "valid": ok, "samples": samples})

    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        live_ok = False
        detail_written = False
        for sid, sname in SECTIONS:
            url = f"{host}/viewforum.php?f={sid}"
            try:
                html = fetch(url)
            except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
                print(f"[FAIL] f={sid:<3} ({sname}) fetch error: {ex}")
                failed = True
                report.append({"f": sid, "name": sname, "valid": False, "error": str(ex)})
                continue

            items = parse_listing(html)
            lp = last_page(html)
            valid = len(items) > 0
            if valid:
                live_ok = True
            status = "OK" if valid else "FAIL"
            if not valid:
                failed = True
            print(f"[{status}] f={sid:<3} ({sname}) topics={len(items)} last_page={lp}")
            for tid, title in items[:2]:
                print(f"         t={tid} {title[:90]}")

            out = fixture_dir / f"forum_f{sid}.html"
            out.write_text(html, encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")

            if not detail_written and items:
                for tid, _title in items[:3]:
                    turl = f"{host}/viewtopic.php?t={tid}"
                    try:
                        dhtml = fetch(turl)
                    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
                        print(f"         topic fetch error: {ex}")
                        continue
                    dok, dtid, dsamples = score_topic(dhtml)
                    print(f"         topic t={tid} ok={dok} id={dtid} {dsamples}")
                    if dok:
                        dout = fixture_dir / "topic_sample.html"
                        dout.write_text(dhtml, encoding="utf-8")
                        print(f"         wrote {dout.relative_to(REPO_ROOT)}")
                        detail_written = True
                        break

            report.append(
                {
                    "f": sid,
                    "name": sname,
                    "topics": len(items),
                    "last_page": lp,
                    "valid": valid,
                    "samples": [t[0] for t in items[:2]],
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
    print("  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Anibelka")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
