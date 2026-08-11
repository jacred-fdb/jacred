#!/usr/bin/env python3
"""
Dry-run RuDub browse.php cards vs JacRed quality gate (HD 1080 / HD 2160 only).

  python3 scripts/dry_run_rudub_parser.py
  python3 scripts/dry_run_rudub_parser.py --user U --password P --refresh-fixtures

Env alternatives: RUDUB_COOKIE, RUDUB_USER / RUDUB_PASSWORD, RUDUB_HOST.

Never commit live cookies/passwords.

Then:
  dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~Rudub
"""

from __future__ import annotations

import argparse
import gzip
import http.cookiejar
import io
import os
import re
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FIXTURE_DIR = REPO_ROOT / "tests" / "JacRed.Tests" / "Fixtures" / "Rudub"
DEFAULT_HOST = "https://r4.rudub.world"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

CARD_SPLIT_RE = re.compile(r'<div\s+class="card__torlist__browse_2"', re.I)
DETAILS_RE = re.compile(
    r'href=["\']/?details\.php\?id=([0-9]+)["\'][^>]*>\s*<b>([\s\S]*?)</b>',
    re.I,
)
DOWNLOAD_RE = re.compile(
    r'href=["\']/?(?:download2\.php\?id=|download\.php\?id=)([0-9]+)["\']',
    re.I,
)
DATE_RE = re.compile(
    r'li\s+title=["\']Дата["\'][^>]*>[\s\S]*?</i>\s*'
    r'([0-9]{4}-[0-9]{2}-[0-9]{2}\s+[0-9]{2}:[0-9]{2}:[0-9]{2})',
    re.I,
)
SIZE_RE = re.compile(
    r'li\s+title=["\']Размер["\'][^>]*>[\s\S]*?</i>\s*([^<]+)',
    re.I,
)
ACTIVITY_RE = re.compile(
    r'li\s+title=["\']Активность["\'][^>]*>[\s\S]*?</i>\s*(\d+)\s*<[\s\S]*?</i>\s*(\d+)',
    re.I,
)
GOOD_Q_RE = re.compile(r"(?i)(?:\b|[^0-9])(?:HD|BD|HDR)?(?:1080p|2160p)\b")
BAD_Q_RE = re.compile(
    r"(?i)(?:\bWEBRip\s*XviD\b|\bWEBRip\s*x264\b|\bHD720p\b|(?<![0-9])720p\b)"
)
BR_RE = re.compile(r"<br\s*/?>", re.I)
SPACES_RE = re.compile(r"[\n\r\t ]+")
STRIP_TAGS_RE = re.compile(r"<[^>]+>")
NAME_ORIG_RE = re.compile(r"^\s*([^\(\n]+?)\s*\(([^)]+)\)\s*")

# Site videoformat: 4 = HD 1080, 5 = HD 2160
PREFERRED_VF = (4, 5)


def clean_title(raw: str) -> str:
    t = BR_RE.sub(" ", raw or "")
    t = STRIP_TAGS_RE.sub("", t)
    t = (
        t.replace("&amp;", "&")
        .replace("&quot;", '"')
        .replace("&#39;", "'")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&nbsp;", " ")
        .replace("\xa0", " ")
    )
    t = SPACES_RE.sub(" ", t).strip()
    for token in ("(Обновляемая)", "(Оновлюється)", "(Золото)"):
        t = re.sub(re.escape(token), "", t, flags=re.I)
    return SPACES_RE.sub(" ", t).strip()


def is_preferred_quality(title: str) -> bool:
    if not title or not title.strip():
        return False
    if BAD_Q_RE.search(title) and not GOOD_Q_RE.search(title):
        return False
    return bool(GOOD_Q_RE.search(title))


def parse_cards(html: str) -> Tuple[List[dict], int, int]:
    """Returns (kept, total_cards, dropped)."""
    parts = CARD_SPLIT_RE.split(html or "")
    kept: List[dict] = []
    total = max(0, len(parts) - 1)
    dropped = 0
    for card in parts[1:]:
        dm = DETAILS_RE.search(card)
        if not dm:
            dropped += 1
            continue
        tid, raw_title = dm.group(1), dm.group(2)
        title = clean_title(raw_title)
        if not is_preferred_quality(title):
            dropped += 1
            continue
        dl = DOWNLOAD_RE.search(card)
        if not dl:
            dropped += 1
            continue
        size_m = SIZE_RE.search(card)
        date_m = DATE_RE.search(card)
        act_m = ACTIVITY_RE.search(card)
        nm = NAME_ORIG_RE.match(title)
        name = nm.group(1).strip() if nm else title.split("(")[0].strip()
        original = nm.group(2).strip() if nm else ""
        kept.append(
            {
                "id": tid,
                "download_id": dl.group(1),
                "title": title,
                "name": name,
                "originalname": original,
                "size": SPACES_RE.sub(" ", size_m.group(1)).strip() if size_m else "",
                "date": date_m.group(1) if date_m else "",
                "sid": int(act_m.group(1)) if act_m else 0,
                "pir": int(act_m.group(2)) if act_m else 0,
            }
        )
    return kept, total, dropped


def decode_body(raw: bytes) -> str:
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    for enc in ("cp1251", "windows-1251", "utf-8"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("cp1251", errors="replace")


def build_opener(cookie: str = "") -> urllib.request.OpenerDirector:
    jar = http.cookiejar.CookieJar()
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    opener = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(jar),
        urllib.request.HTTPSHandler(context=ctx),
    )
    if cookie:
        # Seed Cookie header via a no-op request handler is awkward; keep as default header.
        opener.addheaders = [("User-Agent", UA), ("Cookie", cookie), ("Accept-Encoding", "gzip")]
    else:
        opener.addheaders = [("User-Agent", UA), ("Accept-Encoding", "gzip")]
    return opener


def take_login(opener: urllib.request.OpenerDirector, host: str, user: str, password: str) -> None:
    url = host.rstrip("/") + "/takelogin.php"
    data = urllib.parse.urlencode({"username": user, "password": password}).encode("utf-8")
    req = urllib.request.Request(url, data=data, method="POST")
    with opener.open(req, timeout=45) as resp:
        _ = resp.read()


def fetch(opener: urllib.request.OpenerDirector, url: str) -> str:
    req = urllib.request.Request(url)
    with opener.open(req, timeout=45) as resp:
        return decode_body(resp.read())


def score_fixture(path: Path) -> None:
    html = path.read_text(encoding="utf-8", errors="replace")
    kept, total, dropped = parse_cards(html)
    print(f"{path.name}: cards={total} kept={len(kept)} dropped={dropped}")
    for row in kept[:8]:
        print(
            f"  id={row['id']} dl={row['download_id']} "
            f"{row['name']} / {row['originalname']} | {row['size']} | {row['title'][:80]}"
        )
    bad_sample = []
    for card in CARD_SPLIT_RE.split(html)[1:]:
        dm = DETAILS_RE.search(card)
        if not dm:
            continue
        title = clean_title(dm.group(2))
        if not is_preferred_quality(title):
            bad_sample.append(title[:90])
        if len(bad_sample) >= 3:
            break
    if bad_sample:
        print("  dropped samples:")
        for t in bad_sample:
            print(f"    - {t}")


def refresh_fixtures(
    host: str,
    fixture_dir: Path,
    user: str,
    password: str,
    cookie: str,
) -> None:
    fixture_dir.mkdir(parents=True, exist_ok=True)
    opener = build_opener(cookie)
    if user and password:
        print(f"login as {user} @ {host}")
        take_login(opener, host, user, password)
    elif not cookie:
        print("warning: no cookie/login — browse may show login form", file=sys.stderr)

    chunks: List[str] = []
    for vf in PREFERRED_VF:
        url = f"{host.rstrip('/')}/browse.php?videoformat={vf}&page=0"
        print(f"GET {url}")
        try:
            html = fetch(opener, url)
        except urllib.error.URLError as exc:
            print(f"fetch failed: {exc}", file=sys.stderr)
            raise SystemExit(1) from exc
        if "card__torlist__browse_2" not in html:
            print("validation marker missing — auth or host may be wrong", file=sys.stderr)
            raise SystemExit(2)
        chunks.append(html)

    # Also pull unfiltered page so fixture can assert XviD/720 drops when present.
    try:
        all_html = fetch(opener, f"{host.rstrip('/')}/browse.php?videoformat=0&page=0")
        if "card__torlist__browse_2" in all_html:
            chunks.append(all_html)
    except urllib.error.URLError:
        pass

    # Prefer mixed sample: take cards from vf=0 if it has SD, else concatenate vf 4+5.
    mixed = "\n".join(chunks)
    out = fixture_dir / "listing_sample.html"
    # Keep a compact sample: first ~12 cards from the mixed stream
    parts = CARD_SPLIT_RE.split(mixed)
    sample_parts = parts[1:13]
    body = "\n".join(f'<div class="card__torlist__browse_2"{p}' for p in sample_parts)
    out.write_text(body, encoding="utf-8")
    print(f"wrote {out} ({out.stat().st_size} bytes)")


def main() -> int:
    ap = argparse.ArgumentParser(description="Dry-run RuDub listing parser")
    ap.add_argument("--host", default=os.environ.get("RUDUB_HOST", DEFAULT_HOST))
    ap.add_argument("--fixture-dir", type=Path, default=DEFAULT_FIXTURE_DIR)
    ap.add_argument("--refresh-fixtures", action="store_true")
    ap.add_argument("--user", default=os.environ.get("RUDUB_USER", ""))
    ap.add_argument("--password", default=os.environ.get("RUDUB_PASSWORD", ""))
    ap.add_argument("--cookie", default=os.environ.get("RUDUB_COOKIE", ""))
    args = ap.parse_args()

    if args.refresh_fixtures:
        refresh_fixtures(args.host, args.fixture_dir, args.user, args.password, args.cookie)

    fixture = args.fixture_dir / "listing_sample.html"
    if not fixture.is_file():
        print(f"missing fixture: {fixture}", file=sys.stderr)
        return 1
    score_fixture(fixture)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
