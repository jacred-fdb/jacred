#!/usr/bin/env python3
"""
cffetch — HTTP с TLS-отпечатком Chrome (curl_cffi).

FlareSolverr решает Cloudflare один раз и отдаёт cookie. Дальше страницы
ходят сюда. .NET HttpClient с той же cookie получает 403.

Образ ghcr.io/jacred-fdb/cffetch, network_mode: host, bind 127.0.0.1:8192.
SOCKS должен совпадать с PROXY_URL FlareSolverr (WARP), иначе IP-binding.

  CFFETCH_PROXY=socks5://127.0.0.1:20001 python3 tools/cffetch/app.py
"""

import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from curl_cffi import requests

PORT = int(os.environ.get("CFFETCH_PORT", "8192"))
BIND = os.environ.get("CFFETCH_BIND", "127.0.0.1").strip() or "127.0.0.1"
DEFAULT_IMPERSONATE = os.environ.get("CFFETCH_IMPERSONATE", "chrome136")
DEFAULT_PROXY = os.environ.get("CFFETCH_PROXY", "").strip()
MAX_BODY = 8 * 1024 * 1024

META_CHARSET = re.compile(rb'charset=["\']?\s*([\w-]+)', re.I)


def decode(response):
    enc = (response.encoding or "").lower()

    if not enc or enc in ("iso-8859-1", "ascii"):
        found = META_CHARSET.search(response.content[:4096])
        enc = found.group(1).decode("ascii", "ignore") if found else "utf-8"

    try:
        return response.content.decode(enc, errors="replace")
    except LookupError:
        return response.content.decode("utf-8", errors="replace")


def fetch(task):
    url = task.get("url")
    if not url:
        return {"status": 0, "error": "адрес не указан"}

    headers = {}

    cookies = task.get("cookies")
    if cookies:
        headers["Cookie"] = cookies

    agent = task.get("userAgent")
    if agent:
        headers["User-Agent"] = agent

    kwargs = {
        "headers": headers,
        "impersonate": task.get("impersonate") or DEFAULT_IMPERSONATE,
        "timeout": int(task.get("timeout") or 25),
        "allow_redirects": True,
    }

    proxy = (task.get("proxy") or DEFAULT_PROXY or "").strip()
    if proxy:
        kwargs["proxy"] = proxy

    post = task.get("postData")

    if post is None:
        response = requests.get(url, **kwargs)
    else:
        headers.setdefault("Content-Type", "application/x-www-form-urlencoded")
        response = requests.post(url, data=post, **kwargs)

    mitigated = "cf-mitigated" in {k.lower() for k in response.headers.keys()}

    if len(response.content) > MAX_BODY:
        return {"status": response.status_code, "cfMitigated": mitigated,
                "error": "страница слишком велика", "body": ""}

    return {"status": response.status_code, "cfMitigated": mitigated, "body": decode(response)}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def reply(self, payload, code=200):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.rstrip("/") in ("/health", ""):
            self.reply({
                "ok": True,
                "impersonate": DEFAULT_IMPERSONATE,
                "proxy": bool(DEFAULT_PROXY),
            })
        else:
            self.reply({"status": 0, "error": "только POST /fetch"}, 404)

    def do_POST(self):
        if self.path.rstrip("/") != "/fetch":
            self.reply({"status": 0, "error": "только POST /fetch"}, 404)
            return

        try:
            length = int(self.headers.get("Content-Length") or 0)
            task = json.loads(self.rfile.read(length) or b"{}")
        except Exception as error:
            self.reply({"status": 0, "error": "разбор запроса: %s" % error}, 400)
            return

        try:
            self.reply(fetch(task))
        except Exception as error:
            self.reply({"status": 0, "error": "%s: %s" % (type(error).__name__, error)})

    def log_message(self, fmt, *args):
        sys.stderr.write("cffetch %s\n" % (fmt % args))


if __name__ == "__main__":
    server = ThreadingHTTPServer((BIND, PORT), Handler)
    sys.stderr.write(
        "cffetch слушает %s:%d, отпечаток %s, proxy=%s\n"
        % (BIND, PORT, DEFAULT_IMPERSONATE, DEFAULT_PROXY or "off")
    )
    server.serve_forever()
