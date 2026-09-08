#!/usr/bin/env python3
"""
Dry-run Toloka listing HTML vs JacRed field shape.

  python3 scripts/dry_run_toloka_parser.py
  python3 scripts/dry_run_toloka_parser.py --refresh-fixtures

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Toloka
"""

from __future__ import annotations

import argparse
import gzip
import html as htmlmod
import io
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
from typing import List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Toloka"
DEFAULT_HOST = "https://toloka.to"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

URL_RE = re.compile(
    r'<a href="(?:https?://[^"]+/)?(t[0-9]+)" class="topictitle"', re.I
)
TITLE_RE = re.compile(r'class="topictitle">([^<]+)</a>', re.I)
SID_RE = re.compile(r'<span class="seedmed"[^>]*><b>([0-9]+)</b></span>', re.I)
PIR_RE = re.compile(r'<span class="leechmed"[^>]*><b>([0-9]+)</b></span>', re.I)
SIZE_RE = re.compile(
    r'<a href="(?:https?://[^"]+/)?download\.php[^"]+"[^>]*>([^<]+)</a>', re.I
)
DLID_RE = re.compile(
    r'href="(?:https?://[^"]+/)?download\.php\?id=([0-9]+)"', re.I
)
DATE_RE = re.compile(
    r'class="postdetails">([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})', re.I
)
SIZE_OK_RE = re.compile(r"(?i)[0-9][0-9.,]* (MB|GB|TB|МБ|ГБ|ТБ)")


def decode_text(raw: bytes) -> str:
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    return raw.decode("utf-8", errors="replace")


def clean(s: str) -> str:
    s = htmlmod.unescape(s).replace("\u00a0", " ")
    return re.sub(r"[\n\r\t ]+", " ", s).strip()


def score_page(html: str) -> Tuple[int, int, List[str]]:
    rows = html.split("</tr>")[1:]
    ok = 0
    samples: List[str] = []
    for row in rows:
        if not row.strip() or re.search(r"Збір коштів", row, re.I):
            continue
        url = URL_RE.search(row)
        title = TITLE_RE.search(row)
        sid = SID_RE.search(row)
        pir = PIR_RE.search(row)
        size_m = SIZE_RE.search(row)
        dlid = DLID_RE.search(row)
        date = DATE_RE.search(row)
        size = clean(size_m.group(1)) if size_m else ""
        if size == "0 B" or "завантажити" in size.lower():
            continue
        if not (url and title and sid and pir and size and dlid and date):
            continue
        if not SIZE_OK_RE.search(size):
            continue
        ok += 1
        if len(samples) < 3:
            title_s = clean(title.group(1))[:80]
            samples.append(f"{url.group(1)} sid={sid.group(1)} pir={pir.group(1)} {size} | {title_s}")
    return len(rows), ok, samples


def build_opener():
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    jar = CookieJar()
    return urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(jar),
        urllib.request.HTTPSHandler(context=ctx),
    ), jar


def request(opener, url: str, *, data: Optional[dict] = None, cookie: str = "") -> bytes:
    headers = {"User-Agent": UA, "Accept-Encoding": "gzip"}
    if cookie:
        headers["Cookie"] = cookie
    if data is not None:
        body = urllib.parse.urlencode(data).encode("utf-8")
        headers["Content-Type"] = "application/x-www-form-urlencoded"
        req = urllib.request.Request(url, data=body, headers=headers, method="POST")
    else:
        req = urllib.request.Request(url, headers=headers, method="GET")
    with opener.open(req, timeout=45) as resp:
        return resp.read()


def login(opener, jar: CookieJar, host: str, user: str, password: str) -> str:
    host = host.rstrip("/")
    request(
        opener,
        f"{host}/login.php",
        data={
            "username": user,
            "password": password,
            "autologin": "on",
            "ssl": "on",
            "redirect": "index.php?",
            "login": "Вхід",
        },
    )
    cookies = {c.name: c.value for c in jar}
    sid = cookies.get("toloka_sid")
    data = cookies.get("toloka_data")
    if not sid or not data:
        raise RuntimeError("login failed: no toloka_sid/toloka_data cookies")
    return f"toloka_sid={sid}; toloka_ssl=1; toloka_data={data};"


def main(argv: Optional[List[str]] = None) -> int:
    p = argparse.ArgumentParser(description="Dry-run Toloka listing HTML vs JacRed fields")
    p.add_argument("--host", default=os.environ.get("TOLOKA_HOST", DEFAULT_HOST))
    p.add_argument("--user", default=os.environ.get("TOLOKA_USER", ""))
    p.add_argument("--password", default=os.environ.get("TOLOKA_PASS", ""))
    p.add_argument("--cookie", default=os.environ.get("TOLOKA_COOKIE", ""))
    p.add_argument(
        "--refresh-fixtures",
        action="store_true",
        help="Fetch live f96 and write Fixtures/Toloka/browse_f96.html (needs cookie or login)",
    )
    p.add_argument("--fixture-dir", default=str(DEFAULT_FIXTURE_DIR))
    p.add_argument("--json-out", default="")
    args = p.parse_args(argv)

    fixture_dir = Path(args.fixture_dir)
    fixture = fixture_dir / "browse_f96.html"
    report = []
    failed = False

    if args.refresh_fixtures:
        opener, jar = build_opener()
        host = args.host.rstrip("/")
        cookie = args.cookie
        try:
            if not cookie:
                if not args.user or not args.password:
                    print("error: --refresh-fixtures needs --user/--password or --cookie / TOLOKA_* env", file=sys.stderr)
                    return 1
                cookie = login(opener, jar, host, args.user, args.password)
            html = decode_text(request(opener, f"{host}/f96?sort=8", cookie=cookie))
        except (urllib.error.URLError, urllib.error.HTTPError, RuntimeError) as ex:
            print(f"error: {ex}", file=sys.stderr)
            return 1
        if "<html lang=\"uk\"" not in html:
            print("error: page is not a Ukrainian listing (login failed?)", file=sys.stderr)
            return 1
        fixture_dir.mkdir(parents=True, exist_ok=True)
        fixture.write_text(html, encoding="utf-8")
        print(f"wrote {fixture.relative_to(REPO_ROOT)}")

    print("=== Toloka parser dry-run ===\n")
    if not fixture.is_file():
        print(f"[FAIL] missing fixture {fixture}", file=sys.stderr)
        return 1

    html = fixture.read_text(encoding="utf-8")
    rows, ok, samples = score_page(html)
    rate = round(ok / rows * 100, 1) if rows else 0.0
    valid = ok >= 40
    if not valid:
        failed = True
    status = "OK" if valid else "FAIL"
    print(f"[{status}] cat=96 fixture={fixture.name} rows={rows} ok={ok} rate={rate}%")
    for s in samples:
        print(f"         sample: {s}")

    report.append(
        {
            "id": "96",
            "types": ["movie"],
            "file": fixture.name,
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
