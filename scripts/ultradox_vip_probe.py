#!/usr/bin/env python3
"""Probe ultradox.vip from master: redirects, Referer gate, listing paths, pager tails.

Piped via stdin (`python3 -`) so keep this file self-contained.

  python3 scripts/ultradox_vip_probe.py
  ssh master.jacred.stream 'python3 -' < scripts/ultradox_vip_probe.py
"""

from __future__ import annotations

import gzip
import io
import json
import os
import re
import ssl
import sys
import urllib.error
import urllib.request

HOST = os.environ.get("ULTRADOX_HOST", "https://ultradox.vip").rstrip("/")
OLD_HOST = os.environ.get("ULTRADOX_OLD_HOST", "https://ultradox.onl").rstrip("/")
TASKPARSE = os.environ.get("TASKPARSE", "/opt/jacred/Data/temp/ultradox_taskParse.json")
GOOGLE_REFERER = "https://www.google.com/"
UA = (
    "Mozilla/5.0 (X11; Linux x86_64; rv:153.0) "
    "Gecko/20100101 Firefox/153.0"
)

SECTIONS = ("serial-hd", "hd", "rufilm", "camrip", "webrips", "anime")
ROW_SPLIT = re.compile(r'<tr>\s*<td class="torrent-table-date">')
CANONICAL = re.compile(r'<link\s+rel="canonical"\s+href="([^"]+)"', re.I)
PAGES_BLOCK = re.compile(
    r'<div\s+class="[^"]*\bpages\b[^"]*">([\s\S]*?)</div>', re.I
)
PAGE_NUM = re.compile(r"/page/([0-9]+)/", re.I)
TITLE = re.compile(r"<title>([^<]+)</title>", re.I)
DETAIL_MAGNET = re.compile(
    r"magnet:\?xt=urn:btih:([A-Fa-f0-9]+)&xl=([0-9]+)&dn=",
    re.I,
)


def _ssl():
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    return ctx


def fetch(url: str, referer: str | None = GOOGLE_REFERER, follow: bool = True):
    headers = {
        "User-Agent": UA,
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7",
        "Cache-Control": "no-cache",
        "Pragma": "no-cache",
        "Sec-Fetch-Dest": "document",
        "Sec-Fetch-Mode": "navigate",
        "Sec-Fetch-Site": "cross-site",
        "Sec-Fetch-User": "?1",
        "Upgrade-Insecure-Requests": "1",
        "Accept-Encoding": "gzip",
    }
    if referer:
        headers["Referer"] = referer

    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: A002
            return None

    handlers = [urllib.request.HTTPSHandler(context=_ssl())]
    if not follow:
        handlers.insert(0, NoRedirect())
    opener = urllib.request.build_opener(*handlers)
    req = urllib.request.Request(url, headers=headers)
    chain = [url]
    try:
        with opener.open(req, timeout=45) as resp:
            status = getattr(resp, "status", None) or resp.getcode()
            final = resp.geturl()
            loc = resp.headers.get("Location", "")
            raw = resp.read()
    except urllib.error.HTTPError as ex:
        loc = ex.headers.get("Location", "") if ex.headers else ""
        raw = b""
        try:
            raw = ex.read() or b""
        except Exception:
            raw = b""
        return {
            "ok": False,
            "status": ex.code,
            "final": loc or url,
            "location": loc,
            "len": len(raw),
            "body": _decode(raw),
            "chain": chain + ([loc] if loc else []),
            "error": str(ex),
        }
    except (urllib.error.URLError, TimeoutError, OSError) as ex:
        return {
            "ok": False,
            "status": 0,
            "final": url,
            "location": "",
            "len": 0,
            "body": "",
            "chain": chain,
            "error": str(ex),
        }

    if raw[:2] == b"\x1f\x8b":
        raw = gzip.GzipFile(fileobj=io.BytesIO(raw)).read()
    body = _decode(raw)
    if final and final not in chain:
        chain.append(final)
    return {
        "ok": True,
        "status": status,
        "final": final or url,
        "location": loc,
        "len": len(body),
        "body": body,
        "chain": chain,
        "error": "",
    }


def _decode(raw: bytes) -> str:
    return raw.decode("utf-8", errors="replace")


def row_count(html: str) -> int:
    if not html:
        return 0
    return max(0, len(ROW_SPLIT.split(html)) - 1)


def canonical(html: str) -> str:
    m = CANONICAL.search(html or "")
    return m.group(1) if m else ""


def pager_lasts(html: str, section: str) -> list[int]:
    page_re = re.compile(
        rf"/{re.escape(section.strip('/'))}/page/([0-9]+)/", re.I
    )
    out = []
    for block in PAGES_BLOCK.finditer(html or ""):
        n = 0
        for m in page_re.finditer(block.group(1)):
            n = max(n, int(m.group(1)))
        out.append(n)
    return out


def title_of(html: str) -> str:
    m = TITLE.search(html or "")
    return (m.group(1).strip() if m else "")[:80]


def dump_taskparse(path: str) -> None:
    print(f"\n=== taskParse {path} ===")
    try:
        data = json.loads(open(path, encoding="utf-8").read())
    except OSError as ex:
        print(f"  missing/unreadable: {ex}")
        return
    except json.JSONDecodeError as ex:
        print(f"  invalid json: {ex}")
        return
    if not isinstance(data, dict):
        print(f"  unexpected type {type(data)}")
        return
    for cat, pages in data.items():
        if not isinstance(pages, list):
            print(f"  {cat}: {pages!r}")
            continue
        nums = [p.get("page") for p in pages if isinstance(p, dict)]
        nums = [n for n in nums if isinstance(n, int)]
        done = 0
        for p in pages:
            if not isinstance(p, dict):
                continue
            # trio TaskParse uses updateTime / done flags depending on cycle store
            if p.get("done") or p.get("updateTime"):
                done += 1
        mx = max(nums) if nums else 0
        print(f"  {cat}: count={len(pages)} maxPage={mx} with_updateTime={done}")


def summarize_page(label: str, url: str, r: dict, section: str) -> dict:
    html = r.get("body") or ""
    rows = row_count(html)
    pagers = pager_lasts(html, section)
    # also match nested /nerufilm/{section}/
    if not pagers:
        pagers = pager_lasts(html, f"nerufilm/{section}") if "/" not in section else []
    info = {
        "label": label,
        "url": url,
        "status": r.get("status"),
        "final": r.get("final"),
        "len": r.get("len"),
        "rows": rows,
        "canonical": canonical(html),
        "pagers": pagers,
        "title": title_of(html),
        "error": r.get("error") or "",
        "listingOk_empty_html": not bool(html.strip()),
        "listingOk_zero_rows": bool(html.strip()) and rows == 0,
        "magnets": len(DETAIL_MAGNET.findall(html)),
    }
    print(
        f"  [{label}] status={info['status']} rows={rows} len={info['len']} "
        f"canonical={info['canonical']!r} pagers={pagers} final={info['final']}"
    )
    if info["error"]:
        print(f"         error={info['error']}")
    if info["listingOk_empty_html"]:
        print("         empty body → ParseAll would NOT mark done")
    elif info["listingOk_zero_rows"]:
        print("         HTML + 0 rows → ParseAll WOULD mark done")
    return info


def main() -> int:
    print(f"=== ultradox vip probe host={HOST} ===\n")

    print("--- redirects (no Referer follow=False) ---")
    for url in (HOST + "/", OLD_HOST + "/", HOST + "/serial-hd/"):
        r = fetch(url, referer=None, follow=False)
        print(
            f"  {url}  status={r['status']} location={r.get('location')!r} "
            f"final={r['final']}"
        )

    print("\n--- Referer gate ---")
    gate_url = HOST + "/serial-hd/"
    for label, ref in (("none", None), ("own-origin", HOST + "/"), ("google", GOOGLE_REFERER)):
        r = fetch(gate_url, referer=ref, follow=True)
        html = r.get("body") or ""
        print(
            f"  Referer={label!r} status={r['status']} len={r['len']} "
            f"rows={row_count(html)} final={r['final']}"
        )

    print("\n--- sections ---")
    for section in SECTIONS:
        print(f"\n## {section}")
        short = f"{HOST}/{section}/"
        nested = f"{HOST}/nerufilm/{section}/"
        r_short = fetch(short)
        summarize_page("short", short, r_short, section)
        r_nested = fetch(nested)
        summarize_page("nerufilm", nested, r_nested, section)

        html = r_short.get("body") or ""
        rows = row_count(html)
        live_html = html
        live_url = short
        if rows == 0 and row_count(r_nested.get("body") or "") > 0:
            live_html = r_nested.get("body") or ""
            live_url = nested
            print("  using nerufilm listing as live")

        pagers = pager_lasts(live_html, section)
        if not pagers:
            pagers = pager_lasts(live_html, f"nerufilm/{section}")
        first = pagers[0] if pagers else 0
        footer = pagers[-1] if pagers else 0
        print(f"  pager first={first} footer={footer}")

        base = live_url.rstrip("/")
        for label, page in (
            ("incontent-last", first),
            ("footer-last", footer),
            ("footer-last+1", footer + 1 if footer else 0),
            ("missing-99999", 99999),
        ):
            if page <= 0:
                continue
            u = f"{base}/page/{page}/"
            summarize_page(label, u, fetch(u), section)

        # one detail magnet check on first listing row
        chunks = ROW_SPLIT.split(live_html)
        if len(chunks) > 1:
            m = re.search(r'href="([^"#]+\.html)"', chunks[1], re.I)
            if m:
                dpath = m.group(1)
                durl = dpath if dpath.startswith("http") else HOST + "/" + dpath.lstrip("/")
                dr = fetch(durl)
                magnets = len(DETAIL_MAGNET.findall(dr.get("body") or ""))
                print(
                    f"  [detail] status={dr['status']} magnets={magnets} "
                    f"url={durl} final={dr['final']}"
                )

    dump_taskparse(TASKPARSE)
    print("\nprobe done.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
