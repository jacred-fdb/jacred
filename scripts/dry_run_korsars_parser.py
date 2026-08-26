#!/usr/bin/env python3
"""
Dry-run korsars.pro listing HTML vs JacRed Go-compatible regexes.

Login required for live fetch (phpBB bb_data cookie).

  python3 scripts/dry_run_korsars_parser.py
  python3 scripts/dry_run_korsars_parser.py --user U --password P --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Korsars
"""

from __future__ import annotations

import argparse
import gzip
import http.cookiejar
import io
import json
import os
import re
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Dict, List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Korsars"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

# Keep in sync with KorsarsParser / Go cron/korsars
# Keep in sync with KorsarsCategories.MovieIds/SerialIds/CartoonIds
MOVIE_CATS = ("282", "31", "33", "125", "146", "270")
SERIAL_CATS = ("287", "286", "267", "303", "288", "39", "40", "300", "41", "121", "144", "271")
CARTOON_CATS = ("43", "44", "277", "46", "272", "273")
ALL_CATS = MOVIE_CATS + SERIAL_CATS + CARTOON_CATS
TOPICS_PER_PAGE = 50

ROW_DATE_RE = re.compile(r"<p>([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})</p>")
ROW_TOPIC_ID_RE = re.compile(r'<a id="tt-([0-9]+)"')
ROW_TITLE_RE = re.compile(r'<a id="tt-[0-9]+"[^>]+>\s*<b>([^<]+)</b>\s*</a>')
ROW_SID_RE = re.compile(r'<span class="seedmed"[^>]*><b>([0-9]+)</b>')
ROW_PIR_RE = re.compile(r'<span class="leechmed"[^>]*><b>([0-9]+)</b>')
ROW_SIZE_RE = re.compile(r'href="\./dl\.php\?id=[0-9]+"[^>]*>([^<]+)</a>')
ROW_MAGNET_RE = re.compile(r'href="(magnet:[^"]+)"')
PAGER_START_RE = re.compile(r"viewforum\.php\?f=[0-9]+(?:&amp;|&)start=([0-9]+)")
STRIP_TAGS_RE = re.compile(r"<[^>]+>")
SPACES_RE = re.compile(r"\s+")

YEAR_RE = re.compile(r"\(([0-9]{4})")
TITLE_SERIAL3_RE = re.compile(r"^([^/\[\(]+) / [^/\[\(]+ / ([^/\[\(]+) \[S[0-9]")
TITLE_SERIAL2_RE = re.compile(r"^([^/\[\(]+) / ([^/\[\(]+) \[S[0-9]")
TITLE_SERIAL1_RE = re.compile(r"^([^/\[\(]+) \[S[0-9]")
TITLE_MOVIE3_RE = re.compile(r"^([^/\(]+) / [^/\(]+ / ([^/\(]+) \(")
TITLE_MOVIE2_RE = re.compile(r"^([^/\(]+) / ([^/\(]+) \(")
TITLE_MOVIE1_RE = re.compile(r"^([^/\(]+) \(")


def clean_text(s: str) -> str:
    s = STRIP_TAGS_RE.sub("", s)
    s = (
        s.replace("&amp;", "&")
        .replace("&quot;", '"')
        .replace("&#39;", "'")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&nbsp;", " ")
        .replace("\xa0", " ")
    )
    return SPACES_RE.sub(" ", s).strip()


def match1(re_obj: re.Pattern[str], s: str) -> str:
    m = re_obj.search(s)
    return m.group(1).strip() if m else ""


def parse_title(title: str) -> Tuple[str, str, int]:
    year = 0
    ym = YEAR_RE.search(title or "")
    if ym:
        year = int(ym.group(1))
    for re_obj in (TITLE_SERIAL3_RE, TITLE_SERIAL2_RE):
        m = re_obj.match(title or "")
        if m:
            return m.group(1).strip(), m.group(2).strip(), year
    m = TITLE_SERIAL1_RE.match(title or "")
    if m:
        return m.group(1).strip(), "", year
    for re_obj in (TITLE_MOVIE3_RE, TITLE_MOVIE2_RE):
        m = re_obj.match(title or "")
        if m:
            return m.group(1).strip(), m.group(2).strip(), year
    m = TITLE_MOVIE1_RE.match(title or "")
    if m:
        return m.group(1).strip(), "", year
    return "", "", year


def parse_listing(html: str) -> List[Dict[str, object]]:
    out: List[Dict[str, object]] = []
    parts = html.split('id="tt-')
    for part in parts[1:]:
        row = '<a id="tt-' + part
        tid = match1(ROW_TOPIC_ID_RE, row)
        title = clean_text(match1(ROW_TITLE_RE, row))
        if not tid or not title:
            continue
        date = match1(ROW_DATE_RE, row)
        if not date:
            continue
        size = clean_text(match1(ROW_SIZE_RE, row))
        magnet = clean_text(match1(ROW_MAGNET_RE, row))
        if not size or not magnet:
            continue
        name, original, year = parse_title(title)
        out.append(
            {
                "topic_id": tid,
                "title": title,
                "date": date,
                "size": size,
                "magnet": magnet,
                "sid": match1(ROW_SID_RE, row),
                "pir": match1(ROW_PIR_RE, row),
                "name": name,
                "original": original,
                "year": year,
            }
        )
    return out


def last_page(html: str) -> int:
    max_start = 0
    for m in PAGER_START_RE.finditer(html):
        n = int(m.group(1))
        if n > max_start:
            max_start = n
    return max_start // TOPICS_PER_PAGE


def looks_like_login(html: str) -> bool:
    return 'name="login_username"' in html and 'id="tt-' not in html


def make_opener(cookie_jar: Optional[http.cookiejar.CookieJar] = None) -> urllib.request.OpenerDirector:
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    handlers: List[urllib.request.BaseHandler] = [urllib.request.HTTPSHandler(context=ctx)]
    if cookie_jar is not None:
        handlers.append(urllib.request.HTTPCookieProcessor(cookie_jar))
    return urllib.request.build_opener(*handlers)


def fetch(opener: urllib.request.OpenerDirector, url: str) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Encoding": "gzip"})
    with opener.open(req, timeout=45) as resp:
        raw = resp.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    return raw.decode("utf-8", errors="replace")


def login(host: str, user: str, password: str) -> urllib.request.OpenerDirector:
    jar = http.cookiejar.CookieJar()
    opener = make_opener(jar)
    data = urllib.parse.urlencode(
        {
            "login_username": user,
            "login_password": password,
            "autologin": "1",
            "login": "Вход",
        }
    ).encode("utf-8")
    req = urllib.request.Request(
        host.rstrip("/") + "/login.php",
        data=data,
        headers={
            "User-Agent": UA,
            "Content-Type": "application/x-www-form-urlencoded",
            "Referer": host.rstrip("/") + "/",
        },
        method="POST",
    )
    # Don't follow redirects so we keep Set-Cookie from the login response.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: N802
            return None

    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    login_opener = urllib.request.build_opener(
        urllib.request.HTTPSHandler(context=ctx),
        urllib.request.HTTPCookieProcessor(jar),
        NoRedirect(),
    )
    try:
        with login_opener.open(req, timeout=45) as resp:
            resp.read()
    except urllib.error.HTTPError as ex:
        # 302 with Set-Cookie is success for phpBB login.
        if ex.code not in (301, 302, 303, 307, 308):
            raise

    cookie_names = {c.name for c in jar}
    if "bb_data" not in cookie_names:
        raise RuntimeError(f"login failed — no bb_data cookie (got {sorted(cookie_names)})")
    print(f"         login OK (bb_data present, cookies={sorted(cookie_names)})")
    return opener


def score_fixture(path: Path) -> Tuple[bool, int, int, List[str]]:
    html = path.read_text(encoding="utf-8", errors="replace")
    items = parse_listing(html)
    lp = last_page(html)
    samples = [f"t={it['topic_id']} {str(it['title'])[:70]}" for it in items[:2]]
    ok = len(items) > 0
    return ok, len(items), lp, samples


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run korsars.pro HTML vs JacRed (login for live)")
    p.add_argument("--host", default=os.environ.get("KORSARS_HOST", "https://korsars.pro"))
    p.add_argument("--user", default=os.environ.get("KORSARS_USER", ""))
    p.add_argument("--password", default=os.environ.get("KORSARS_PASSWORD", ""))
    p.add_argument("--refresh-fixtures", action="store_true")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    p.add_argument(
        "--cats",
        default="282,287,43",
        help="Comma-separated forum ids to refresh (default: one movie/serial/cartoon)",
    )
    args = p.parse_args(argv)

    host = args.host.rstrip("/")
    fixture_dir = Path(args.fixture_dir)
    report = []
    failed = False

    print(f"=== korsars parser dry-run ({len(ALL_CATS)} cats) ===\n")

    for name in ("listing_movie.html", "listing_serial.html"):
        path = fixture_dir / name
        if not path.is_file():
            print(f"[WARN] fixture {name} missing — run with --refresh-fixtures")
            continue
        ok, n, lp, samples = score_fixture(path)
        status = "OK" if ok else "FAIL"
        if not ok:
            failed = True
        print(f"[{status}] fixture {name} topics={n} last_page={lp}")
        for s in samples:
            print(f"         {s}")
        report.append({"fixture": name, "topics": n, "last_page": lp, "valid": ok})

    # Quick title unit checks (Go shape).
    title_cases = [
        ("Начало / Inception (2010) BDRip", "Начало", "Inception", 2010),
        ("Игра престолов / Game of Thrones [S01] (2011)", "Игра престолов", "Game of Thrones", 2011),
        ("Чернобыль [S01] (2019)", "Чернобыль", "", 2019),
    ]
    for title, wn, wo, wy in title_cases:
        name, original, year = parse_title(title)
        ok = (name, original, year) == (wn, wo, wy)
        if not ok:
            failed = True
        status = "OK" if ok else "FAIL"
        print(f"[{status}] parse_title {title[:50]!r} -> {name!r}/{original!r}/{year}")

    if args.refresh_fixtures:
        user = (args.user or "").strip()
        password = args.password or ""
        if not user:
            print("\n[FAIL] --refresh-fixtures needs --user/--password (or KORSARS_USER/KORSARS_PASSWORD)", file=sys.stderr)
            return 2

        fixture_dir.mkdir(parents=True, exist_ok=True)
        try:
            opener = login(host, user, password)
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError, RuntimeError) as ex:
            print(f"[FAIL] login error: {ex}", file=sys.stderr)
            return 2

        cats = [c.strip() for c in args.cats.split(",") if c.strip()]
        written = 0
        for cat in cats:
            url = f"{host}/viewforum.php?f={cat}"
            try:
                html = fetch(opener, url)
            except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
                print(f"[FAIL] f={cat} fetch error: {ex}")
                failed = True
                report.append({"f": cat, "valid": False, "error": str(ex)})
                continue

            if looks_like_login(html):
                print(f"[FAIL] f={cat} returned login form")
                failed = True
                report.append({"f": cat, "valid": False, "error": "login form"})
                continue

            items = parse_listing(html)
            lp = last_page(html)
            valid = len(items) > 0
            status = "OK" if valid else "FAIL"
            if not valid:
                failed = True
            print(f"[{status}] f={cat:<3} topics={len(items)} last_page={lp}")
            for it in items[:2]:
                print(f"         t={it['topic_id']} {str(it['title'])[:90]}")

            if cat in MOVIE_CATS and written == 0:
                out = fixture_dir / "listing_movie.html"
            elif cat in SERIAL_CATS:
                out = fixture_dir / "listing_serial.html"
            elif cat in CARTOON_CATS:
                out = fixture_dir / "listing_cartoon.html"
            else:
                out = fixture_dir / f"listing_f{cat}.html"

            out.write_text(html, encoding="utf-8")
            print(f"         wrote {out.relative_to(REPO_ROOT)}")
            written += 1
            report.append(
                {
                    "f": cat,
                    "topics": len(items),
                    "last_page": lp,
                    "valid": valid,
                    "samples": [it["topic_id"] for it in items[:2]],
                }
            )

    if args.json_out:
        Path(args.json_out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    if failed and not args.refresh_fixtures:
        print("Dry-run FAILED.", file=sys.stderr)
        return 2

    print("Dry-run done. Run:")
    print("  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Korsars")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
