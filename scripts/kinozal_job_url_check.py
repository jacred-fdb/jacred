#!/usr/bin/env python3
"""Compare JacRed ParseAll URL vs the live Kinozal search form.

Log:     /browse.php?c=13&page=9&d=2020&t=1
Browser: /browse.php?s=&g=0&c=13&v=0&d=2020&w=0&t=1&f=0

Browser submit has no page (page 0). t=1 is sort «Сидам», not a filter.

  ssh master.jacred.stream 'python3 -' < scripts/kinozal_job_url_check.py
"""

from __future__ import annotations

import json
import os
import re
import time
import urllib.request

FS_URL = os.environ.get("FS_URL", "http://127.0.0.1:8191/v1")
SESSION = os.environ.get("FS_SESSION", "jacred-kinozal_guru")
HOST = os.environ.get("KINOZAL_HOST", "https://kinozal.guru").rstrip("/")
SLEEP = float(os.environ.get("SLEEP", "2.5"))

LISTING_HREF = re.compile(r"""href=["']/details\.php\?id=(\d+)["']""", re.I)
EMPTY_HINT = re.compile(r"Нет активных раздач")
FOUND = re.compile(r"Найдено\s+(\d+)")
PAGER_JACRED = re.compile(r'>([0-9]+)</a></li><li><a rel="next"', re.I)
PAGER_HREF = re.compile(
    r'href=["\'](\?[^"\']*page=\d+[^"\']*)["\']',
    re.I,
)
SELECT = re.compile(
    r'<select name=["\']?(c|d|t|w|v|g|f)["\']?[^>]*>(.*?)</select>',
    re.I | re.S,
)
SELECTED = re.compile(r"<option([^>]*)>", re.I)
VALUE = re.compile(r"""value=["']?(\d+)""", re.I)


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
    return int(sol.get("status") or 0), sol.get("response") or "", dt


def selected_values(html: str) -> dict[str, str]:
    out: dict[str, str] = {}
    for name, body in SELECT.findall(html or ""):
        picked = None
        for opt in SELECTED.finditer(body):
            attrs = opt.group(1)
            if "selected" not in attrs.lower():
                continue
            vm = VALUE.search(attrs)
            if vm:
                picked = vm.group(1)
                break
        if picked is not None:
            out[name.lower()] = picked
    return out


def summarize(html: str) -> dict:
    ids = LISTING_HREF.findall(html or "")
    found = FOUND.search(html or "")
    sel = selected_values(html)
    hrefs = PAGER_HREF.findall(html or "")
    return {
        "len": len(html or ""),
        "t_peer": "t_peer" in (html or ""),
        "empty": bool(EMPTY_HINT.search(html or "")),
        "found": (int(found.group(1)) if found else None),
        "hrefs": len(ids),
        "first_id": (ids[0] if ids else None),
        "sel": sel,
        "pager_digit": (int(m.group(1)) if (m := PAGER_JACRED.search(html or "")) else None),
        "pager_sample": (hrefs[0][:90] if hrefs else None),
        "logged_in": ">Выход</a>" in (html or ""),
    }


def probe(label: str, path_q: str) -> dict:
    url = f"{HOST}/browse.php?{path_q}"
    http, html, dt = fs_get(url)
    row = summarize(html)
    sel = row["sel"]
    print(
        f"{label:18} {dt:5.2f}s len={row['len']:<6} empty={str(row['empty']):5} "
        f"t_peer={str(row['t_peer']):5} hrefs={row['hrefs']:<3} found={str(row['found']):<6} "
        f"c/d/t/w={sel.get('c')}/{sel.get('d')}/{sel.get('t')}/{sel.get('w')} "
        f"pager={row['pager_digit']} next={row['pager_sample']} id={row['first_id']}"
    )
    print(f"{'':18} url=.../browse.php?{path_q}")
    time.sleep(SLEEP)
    return row


def main() -> int:
    print(f"FS_URL={FS_URL} session={SESSION}")
    print(
        "Params on the site form:\n"
        "  s  search string (empty = no text search)\n"
        "  g  where: 0 title, 1 person, 2 genres, 3 regex\n"
        "  c  category (13 = Кино - Фантастика)\n"
        "  v  format: 0 all\n"
        "  d  release year (0 = all years)\n"
        "  w  extra filter: 0 none, 1 today, 11 gold, ...\n"
        "  t  SORT: 0 Залит (date), 1 Сидам (seeders), 3 size\n"
        "  f  order: 0 desc, 1 asc\n"
        "  page  0-based listing page (browser submit omits it → 0)\n"
    )
    probes = [
        ("jacred_log_p9", "c=13&page=9&d=2020&t=1"),
        ("browser_form", "s=&g=0&c=13&v=0&d=2020&w=0&t=1&f=0"),
        ("browser_plus_p9", "s=&g=0&c=13&v=0&d=2020&w=0&t=1&f=0&page=9"),
        ("jacred_p0", "c=13&page=0&d=2020&t=1"),
        ("update_tasks", "c=13&d=2020&t=1"),
        ("hourly_parse", "c=13&page=0"),
        ("browser_t0", "s=&g=0&c=13&v=0&d=2020&w=0&t=0&f=0"),
    ]
    rows = [probe(label, q) for label, q in probes]
    log_p9 = rows[0]
    browser = rows[1]
    print("\n== verdict ==")
    print(
        f"page0 listing={not browser['empty'] and browser['t_peer']} "
        f"found={browser['found']} pager_digit={browser['pager_digit']}"
    )
    print(
        f"jacred page=9 empty={log_p9['empty']} hrefs={log_p9['hrefs']} "
        f"vs browser+page9 empty={rows[2]['empty']} hrefs={rows[2]['hrefs']}"
    )
    if log_p9["empty"] and browser["t_peer"] and not browser["empty"]:
        digit = browser["pager_digit"]
        print(
            "page=9 of a year list is not the same request as the browser form "
            "(form has no page → page 0). If pager_digit <= 9, page=9 is past last listing."
        )
        if digit is not None:
            print(f"enqueue should be page 0..{digit - 1}, not page={digit} or higher.")
    same_p0 = (
        rows[3]["first_id"]
        and rows[3]["first_id"] == rows[1]["first_id"] == rows[4]["first_id"]
    )
    print(f"p0 first_id match jacred/browser/UpdateTasks: {same_p0} ({rows[3]['first_id']})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
