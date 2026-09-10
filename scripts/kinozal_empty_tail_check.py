#!/usr/bin/env python3
"""Probe Kinozal year-list tails and emulate JacRed parsePage gates.

Confirms whether ParseAll `browse stale/empty shell` (len≈15269, no t_peer)
is an empty search («Нет активных раздач») rather than a CF tab miss — and
whether C# would mark the page done.

Gates mirror Infrastructure/Trackers/Kinozal/KinozalParser.cs + parsePage
(no magnet/hash fetch: listing pages report needs_hash). Piped via stdin
(`python3 -`) so keep this file self-contained.

  python3 scripts/kinozal_empty_tail_check.py
  python3 scripts/kinozal_empty_tail_check.py --fixtures
  ssh master.jacred.stream 'python3 -' < scripts/kinozal_empty_tail_check.py
"""

from __future__ import annotations

import argparse
import json
import os
import re
import time
import urllib.request
from pathlib import Path

FS_URL = os.environ.get("FS_URL", "http://127.0.0.1:8191/v1")
SESSION = os.environ.get("FS_SESSION", "jacred-kinozal_guru")
HOST = os.environ.get("KINOZAL_HOST", "https://kinozal.guru").rstrip("/")
TASKPARSE = os.environ.get("TASKPARSE", "/opt/jacred/Data/temp/kinozal_taskParse.json")
SLEEP = float(os.environ.get("SLEEP", "3"))

PAGER_JACRED = re.compile(r'>([0-9]+)</a></li><li><a rel="next"', re.I)
PAGER_HREFS = re.compile(
    r'href=["\'][^"\']*browse\.php\?[^"\']*page=(\d+)[^"\']*["\']',
    re.I,
)
EMPTY_HINT = re.compile(r"Нет активных раздач")
# C# TorrentListingHref — not a loose /details.php (that hits userdetails.php).
LISTING_HREF = re.compile(r"""href=["']/details\.php\?id=\d+""", re.I)
TITLE = re.compile(r"<title>([^<]+)</title>", re.I)
ROW_SPLIT = re.compile(r"""<tr class=["']?(?:first )?bg["']?>""", re.I)
ATTR_Q = r"""["']?"""
NAM_HREF = re.compile(
    rf"""<td class={ATTR_Q}nam{ATTR_Q}>\s*<a href=["']/details\.php\?id=(\d+)["']""",
    re.I,
)
try:
    REPO_ROOT = Path(__file__).resolve().parents[1]
except NameError:
    REPO_ROOT = Path.cwd()


def is_transient(html: str) -> bool:
    if not (html or "").strip():
        return True
    low = html.lower()
    if "just a moment" in low or "один момент" in html:
        return True
    return len(html) < 2000 and "503 service temporarily unavailable" in low


def is_logged_in(html: str) -> bool:
    return bool(html) and ">Выход</a>" in html


def has_kinozal_title(html: str) -> bool:
    if not html:
        return False
    m = TITLE.search(html)
    if m and "Кинозал" in m.group(1):
        return True
    return "Кинозал.GURU" in html or "Кинозал.ТВ" in html


def is_login_wall(html: str) -> bool:
    if not (html or "").strip() or is_transient(html) or is_logged_in(html):
        return False
    low = html.lower()
    return "takelogin.php" in low or "take_login" in low or 'name="username"' in low


def is_valid_browse(html: str) -> bool:
    return bool((html or "").strip()) and "t_peer" in html and has_kinozal_title(html)


def is_empty_search(html: str) -> bool:
    if is_transient(html) or is_login_wall(html):
        return False
    if "t_peer" in html:
        return False
    if not is_logged_in(html) or not has_kinozal_title(html):
        return False
    return "Нет активных раздач" in html


def is_stale_listing(html: str) -> bool:
    if is_transient(html) or is_login_wall(html) or is_empty_search(html):
        return False
    if "t_peer" in html:
        return False
    return is_logged_in(html)


def year_task_page_count(pager_digit_or_html) -> int:
    if isinstance(pager_digit_or_html, int):
        return 1 if pager_digit_or_html <= 0 else pager_digit_or_html
    html = pager_digit_or_html or ""
    m = PAGER_JACRED.search(html)
    if not m:
        return 1
    return year_task_page_count(int(m.group(1)))


def count_listing_hrefs(html: str) -> int:
    return len(LISTING_HREF.findall(html or ""))


def count_parsed_rows(html: str) -> int:
    """Same field gates as ParseTorrentsFromPage (id + title + sid + pir + size + time)."""
    if not html:
        return 0
    parsed = 0
    for row in ROW_SPLIT.split(html)[1:]:
        if not row.strip():
            continue
        if not NAM_HREF.search(row) and not LISTING_HREF.search(row):
            continue
        title = re.search(rf"class={ATTR_Q}r[0-9]+{ATTR_Q}>([^<]+)</a>", row, re.I)
        sid = re.search(rf"<td class={ATTR_Q}sl_s{ATTR_Q}>([0-9]+)</td>", row, re.I)
        pir = re.search(rf"<td class={ATTR_Q}sl_p{ATTR_Q}>([0-9]+)</td>", row, re.I)
        size = re.search(rf"<td class={ATTR_Q}s{ATTR_Q}>([0-9\.,]+ (?:МБ|ГБ|ТБ))</td>", row, re.I)
        listing_time = re.search(
            rf"<td class={ATTR_Q}sl_p{ATTR_Q}>[0-9]+</td>\s*<td class={ATTR_Q}s{ATTR_Q}>([^<]+)</td>",
            row,
            re.I,
        )
        if all([title, sid, pir, size, listing_time]):
            parsed += 1
    return parsed


def should_mark_page_done(parsed: int, resolved: int, listing_hrefs: int) -> bool:
    if listing_hrefs > 0 and parsed <= 0:
        return False
    if parsed <= 0:
        return True
    return resolved >= parsed


def parse_page_verdict(html: str) -> dict:
    """Mirror KinozalSyncService.parsePage after HTML is in hand (no FS retries, no hash)."""
    hrefs = count_listing_hrefs(html)
    parsed = count_parsed_rows(html)
    pager = year_task_page_count(html)
    base = {
        "transient": is_transient(html),
        "empty_search": is_empty_search(html),
        "stale": is_stale_listing(html),
        "login_wall": is_login_wall(html),
        "valid": is_valid_browse(html),
        "logged_in": is_logged_in(html),
        "listing_hrefs": hrefs,
        "parsed": parsed,
        "year_task_pages": pager,
        "needs_hash": 0,
    }
    if is_transient(html):
        return {**base, "gate": "transient", "mark_done": False}
    if is_empty_search(html):
        return {**base, "gate": "empty_search", "mark_done": True}
    if is_stale_listing(html):
        return {**base, "gate": "stale", "mark_done": False}
    if is_login_wall(html) or (is_valid_browse(html) and not is_logged_in(html)):
        return {**base, "gate": "login_wall", "mark_done": False}
    if not is_valid_browse(html):
        return {**base, "gate": "invalid", "mark_done": False}
    if hrefs > 0 and parsed <= 0:
        return {**base, "gate": "parse_miss", "mark_done": False}
    if parsed <= 0:
        return {**base, "gate": "empty_listing", "mark_done": True}
    return {
        **base,
        "gate": "listing",
        "mark_done": should_mark_page_done(parsed, parsed, hrefs),
        "needs_hash": parsed,
    }


def stats(html: str) -> dict:
    title = TITLE.search(html)
    verdict = parse_page_verdict(html)
    return {
        "len": len(html),
        "t_peer": "t_peer" in html,
        "details": verdict["listing_hrefs"],
        "parsed": verdict["parsed"],
        "logout": is_logged_in(html),
        "empty_hint": bool(EMPTY_HINT.search(html or "")),
        "jacred_maxpages": (int(m.group(1)) if (m := PAGER_JACRED.search(html or "")) else None),
        "href_page_max": (
            max(int(x) for x in PAGER_HREFS.findall(html)) if html and PAGER_HREFS.search(html) else None
        ),
        "year_task_pages": verdict["year_task_pages"],
        "title": (title.group(1).strip()[:60] if title else ""),
        "gate": verdict["gate"],
        "mark_done": verdict["mark_done"],
        "needs_hash": verdict["needs_hash"],
    }


def browse(cat: str, page: int, year: int | None) -> str:
    q = f"c={cat}&page={page}"
    if year is not None:
        q += f"&d={year}&t=1"
    return f"{HOST}/browse.php?{q}"


def load_span(cat: str, year: int) -> tuple[int, int] | None:
    p = Path(TASKPARSE)
    if not p.exists():
        return None
    data = json.loads(p.read_text(encoding="utf-8"))
    pages = data.get(str(cat), {}).get(f"&d={year}&t=1") or []
    nums = sorted(x.get("page", 0) if isinstance(x, dict) else int(x) for x in pages)
    if not nums:
        return None
    return min(nums), max(nums)


def fs_get(url: str) -> tuple[int, str, float]:
    payload = json.dumps(
        {
            "cmd": "request.get",
            "session": SESSION,
            "url": url,
            "maxTimeout": 60000,
        }
    ).encode()
    req = urllib.request.Request(
        FS_URL, data=payload, headers={"Content-Type": "application/json"}
    )
    t0 = time.time()
    with urllib.request.urlopen(req, timeout=90) as resp:
        root = json.loads(resp.read().decode())
    dt = time.time() - t0
    sol = root.get("solution") or {}
    html = sol.get("response") or ""
    return int(sol.get("status") or 0), html, dt


def print_row(label: str, dt: float, row: dict) -> None:
    done = "done" if row["mark_done"] else "retry"
    print(
        f"{label:22} {dt:5.2f}s len={row['len']:<6} t_peer={str(row['t_peer']):5} "
        f"hrefs={row['details']:<3} parsed={row['parsed']:<3} empty={row['empty_hint']} "
        f"gate={row['gate']:<14} {done:<5} "
        f"pages={row['year_task_pages']} jacred_max={row['jacred_maxpages']} href_max={row['href_page_max']}"
    )


def probe(label: str, url: str) -> dict:
    http, html, dt = fs_get(url)
    row = {"label": label, "url": url.split("browse.php", 1)[-1], "http": http, "sec": round(dt, 2)}
    row.update(stats(html))
    print_row(label, dt, row)
    time.sleep(SLEEP)
    return row


def run_fixtures(root: Path) -> int:
    fixture_dir = root / "tests" / "JacRed.Tests" / "Fixtures" / "Kinozal"
    empty = fixture_dir / "browse_empty_search.html"
    listing = fixture_dir / "browse_c22.html"
    failed = False
    print(f"== local fixtures in {fixture_dir} ==")
    for path in (empty, listing):
        html = path.read_text(encoding="utf-8")
        row = stats(html)
        print_row(path.name, 0.0, row)
        if path.name.startswith("browse_empty") and not (
            row["gate"] == "empty_search" and row["mark_done"]
        ):
            print(f"FAIL: {path.name} expected empty_search/done")
            failed = True
        if path.name.startswith("browse_c22") and not (
            row["gate"] == "listing" and row["parsed"] > 0 and row["year_task_pages"] == 100
        ):
            print(f"FAIL: {path.name} expected listing with pager 100")
            failed = True
    return 1 if failed else 0


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Kinozal empty-tail + parsePage emulate")
    p.add_argument(
        "--fixtures",
        action="store_true",
        help="Run C# gate emulate on local fixtures (no FlareSolverr)",
    )
    args = p.parse_args(argv)

    if args.fixtures:
        return run_fixtures(REPO_ROOT)

    print(f"FS_URL={FS_URL} session={SESSION}")
    cases = [
        ("45", 2026),
        ("45", 2025),
        ("17", 2008),
        ("6", 2013),
        ("15", 2021),
    ]
    print("\n== taskParse spans vs live FS + parsePage emulate ==")
    print(
        "gate=empty_search → C# ParseAll mark done; stale → retry/recycle; "
        "listing → parse rows then get_srv_details hash"
    )
    for cat, year in cases:
        span = load_span(cat, year)
        print(f"\n-- cat={cat} year={year} taskParse={span} --")
        probed_pages: set[int] = set()

        def probe_page(label: str, page: int) -> dict:
            probed_pages.add(page)
            return probe(label, browse(cat, page, year))

        p0 = probe_page(f"c{cat}_y{year}_p0", 0)
        if span:
            lo, hi = span
            want_pages = p0["year_task_pages"]
            old_inclusive = p0["jacred_maxpages"]
            if old_inclusive is not None:
                print(
                    f"  pager: new enqueue 0..{want_pages - 1} "
                    f"(old was 0..{old_inclusive} inclusive); taskParse max={hi}"
                )
            if hi > 0:
                probe_page(f"c{cat}_y{year}_p{hi-1}", hi - 1)
            probe_page(f"c{cat}_y{year}_p{hi}_TAIL", hi)
            probe_page(f"c{cat}_y{year}_p{hi+1}_PAST", hi + 1)
            last_listing = max(want_pages - 1, 0)
            if last_listing not in probed_pages:
                probe_page(f"c{cat}_y{year}_p{last_listing}_NEWLAST", last_listing)
        else:
            print("  (no taskParse on this host)")

    print("\n== hourly page=0 (no year) ==")
    probe("hourly_c45_p0", browse("45", 0, None))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
