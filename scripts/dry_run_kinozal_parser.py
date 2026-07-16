#!/usr/bin/env python3
"""
Dry-run Kinozal browse pages against JacRed category map + HTML field shape.

Also refreshes unit-test fixtures used by JacRed.Tests:

  python3 scripts/dry_run_kinozal_parser.py \\
    --host https://kinozal.guru --user USER --password PASS --refresh-fixtures

  # then verify C# parser:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter Kinozal
"""

from __future__ import annotations

import argparse
import json
import os
import re
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request
from http.cookiejar import CookieJar
from pathlib import Path
from typing import Dict, List, Optional, Tuple

# Keep in sync with Infrastructure/Trackers/Kinozal/KinozalCategories.Map
OUR_CATEGORIES: Dict[str, str] = {
    "45": "serial",
    "46": "serial",
    "8": "movie",
    "6": "movie",
    "15": "movie",
    "17": "movie",
    "35": "movie",
    "39": "movie",
    "13": "movie",
    "14": "movie",
    "24": "movie",
    "11": "movie",
    "9": "movie",
    "47": "movie",
    "12": "movie",
    "10": "movie",
    "7": "movie",
    "16": "movie",
    "18": "doc",
    "37": "sport",
    "49": "tvshow",
    "50": "tvshow",
    "21": "mult",
    "22": "mult",
    "20": "anime",
}

ROW_SPLIT = re.compile(r"<tr class=(?:'first bg'|bg)>")
REL_DATE = re.compile(r"(сегодня|вчера) в ([0-9]{2}:[0-9]{2})", re.I)

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36"
)

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Kinozal"


def build_opener(insecure_ssl: bool = True):
    ctx = ssl.create_default_context()
    if insecure_ssl:
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
    jar = CookieJar()
    return urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(jar),
        urllib.request.HTTPSHandler(context=ctx),
    ), jar


def request(opener, url: str, *, host: str, data: Optional[dict] = None, timeout: int = 30) -> bytes:
    headers = {
        "User-Agent": UA,
        "Cache-Control": "no-cache",
        "DNT": "1",
        "Origin": host.rstrip("/"),
        "Pragma": "no-cache",
        "Referer": f"{host.rstrip('/')}/",
        "Upgrade-Insecure-Requests": "1",
    }
    if data is not None:
        body = urllib.parse.urlencode(data).encode("utf-8")
        headers["Content-Type"] = "application/x-www-form-urlencoded"
        req = urllib.request.Request(url, data=body, headers=headers, method="POST")
    else:
        req = urllib.request.Request(url, headers=headers, method="GET")
    with opener.open(req, timeout=timeout) as resp:
        return resp.read()


def decode_page(raw: bytes) -> str:
    return raw.decode("cp1251", errors="replace")


def login(opener, jar: CookieJar, host: str, user: str, password: str) -> None:
    host = host.rstrip("/")
    request(
        opener,
        f"{host}/takelogin.php",
        host=host,
        data={"username": user, "password": password, "returnto": ""},
    )
    cookies = {c.name: c.value for c in jar}
    if "uid" not in cookies or "pass" not in cookies:
        raise RuntimeError("login failed: no uid/pass cookies")


def stabilize_dates(html: str) -> str:
    def repl(m: re.Match) -> str:
        day = "16.07.2024" if m.group(1).lower() == "сегодня" else "15.07.2024"
        return f"{day} в {m.group(2)}"

    return REL_DATE.sub(repl, html)


def score_page(html: str) -> Tuple[int, int, List[str]]:
    rows = ROW_SPLIT.split(html)[1:]
    ok = 0
    samples: List[str] = []
    for row in rows:
        url = re.search(r'href="/(details\.php\?id=[0-9]+)"', row)
        title = re.search(r'class="r[0-9]+">([^<]+)</a>', row)
        sid = re.search(r"<td class='sl_s'>([0-9]+)</td>", row)
        pir = re.search(r"<td class='sl_p'>([0-9]+)</td>", row)
        size = re.search(r"<td class='s'>([0-9\.,]+ (?:МБ|ГБ))</td>", row)
        time = re.search(r"<td class='sl_p'>[0-9]+</td>\s*<td class='s'>([^<]+)</td>", row)
        if all([url, title, sid, pir, size, time]):
            ok += 1
            if len(samples) < 2:
                samples.append(re.sub(r"\s+", " ", title.group(1)).strip()[:90])
    return len(rows), ok, samples


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run Kinozal browse HTML vs JacRed categories")
    p.add_argument("--host", default=os.environ.get("KINOZAL_HOST", "https://kinozal.guru"))
    p.add_argument("--user", default=os.environ.get("KINOZAL_USER", ""))
    p.add_argument("--password", default=os.environ.get("KINOZAL_PASS", ""))
    p.add_argument("--cookie", default=os.environ.get("KINOZAL_COOKIE", ""))
    p.add_argument("--refresh-fixtures", action="store_true",
                   help="Write browse_c{id}.html for every mapped category under Fixtures/Kinozal")
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    args = p.parse_args(argv)

    opener, jar = build_opener()
    host = args.host.rstrip("/")

    try:
        if args.cookie:
            class CookieOpener:
                def __init__(self, inner, cookie_header: str):
                    self.inner = inner
                    self.cookie_header = cookie_header

                def open(self, req, timeout=None):
                    req.add_header("Cookie", self.cookie_header)
                    return self.inner.open(req, timeout=timeout)

            opener = CookieOpener(opener, args.cookie)
        else:
            if not args.user or not args.password:
                print("error: need --user/--password or --cookie", file=sys.stderr)
                return 1
            login(opener, jar, host, args.user, args.password)
    except (urllib.error.URLError, urllib.error.HTTPError, RuntimeError) as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 1

    cats = OUR_CATEGORIES
    fixture_dir = Path(args.fixture_dir)
    if args.refresh_fixtures:
        fixture_dir.mkdir(parents=True, exist_ok=True)
        # Drop stale fixture names from older naming schemes.
        for stale in fixture_dir.glob("browse_c*.html"):
            stale.unlink()

    report = []
    print(f"=== Kinozal parser dry-run ({len(cats)} categories) ===\n")
    failed = False
    for cat, label in cats.items():
        raw = request(opener, f"{host}/browse.php?c={cat}&page=0", host=host)
        html = decode_page(raw)
        rows, ok, samples = score_page(html)
        rate = round(ok / rows * 100, 1) if rows else 0.0
        valid = "t_peer" in html and "details.php?id=" in html and rows > 0 and rate >= 90
        if not valid:
            failed = True
        status = "OK" if valid else "FAIL"
        print(f"[{status}] c={cat:>3} {label:<10} rows={rows:3} fields_ok={ok:3} rate={rate:5.1f}%")
        for s in samples:
            print(f"         · {s}")

        entry = {
            "cat": cat,
            "label": label,
            "rows": rows,
            "fields_ok": ok,
            "rate": rate,
            "valid": valid,
            "samples": samples,
        }
        report.append(entry)

        if args.refresh_fixtures:
            path = fixture_dir / f"browse_c{cat}.html"
            path.write_text(stabilize_dates(html), encoding="utf-8")
            print(f"         wrote {path.relative_to(REPO_ROOT)}")

    print()
    if args.json_out:
        Path(args.json_out).parent.mkdir(parents=True, exist_ok=True)
        Path(args.json_out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Wrote {args.json_out}")

    if failed:
        print("Dry-run FAILED: one or more categories have broken browse HTML shape.", file=sys.stderr)
        return 2

    print("Dry-run OK. Run C# parser checks with:")
    print("  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Kinozal")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
