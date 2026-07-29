#!/usr/bin/env python3
"""
Pretty systemd timer checker for generated JacRed cron jobs.

Reads all jacred-job-*.timer units from:
  - $MANAGED_DIR (default /etc/systemd/jacred-cron)
  - fallback: systemctl list-unit-files

Prints:
  - Target state
  - Table of Job/Active/EnabledState/Next/Last/OnCalendar
  - Summary counts
"""

from __future__ import annotations

import glob
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime


MANAGED_DIR = os.environ.get("MANAGED_DIR", "/etc/systemd/jacred-cron")
JOBS_TARGET = os.environ.get("JOBS_TARGET", "jacred-jobs.target")


def run(cmd: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(cmd, check=False, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def require_systemctl() -> None:
    if shutil.which("systemctl") is None:
        print("systemctl not found (no systemd on this host?)", file=sys.stderr)
        sys.exit(1)


def systemctl_show(unit: str, prop: str) -> str:
    p = run(["systemctl", "show", unit, "-p", prop, "--value"])
    val = (p.stdout or "").strip()
    if val == "":
        return ""
    return val


def list_timer_units() -> list[str]:
    units: list[str] = []
    if os.path.isdir(MANAGED_DIR):
        for f in sorted(glob.glob(os.path.join(MANAGED_DIR, "jacred-job-*.timer"))):
            units.append(os.path.basename(f))
        return units

    # Fallback discovery.
    p = run(["systemctl", "list-unit-files", "--type=timer", "jacred-job-*.timer"])
    if p.returncode != 0:
        return []

    for line in (p.stdout or "").splitlines():
        line = line.strip()
        if not line or line.startswith("UNIT FILE"):
            continue
        # Expected: UNIT FILE  STATE
        parts = re.split(r"\s+", line)
        if parts:
            units.append(parts[0])
    return sorted(set(units))


def us_to_local_human(us: str) -> str:
    if not us or us in ("n/a", "N/A", "na"):
        return "--"
    if not re.fullmatch(r"\d+", us):
        return us
    sec = int(us) // 1_000_000
    try:
        dt = datetime.fromtimestamp(sec)
        return dt.strftime("%Y-%m-%d %H:%M:%S")
    except (OSError, OverflowError, ValueError):
        return str(sec)


@dataclass
class JobRow:
    job: str
    timer: str
    active_state: str
    sub_state: str
    unit_file_state: str
    on_calendar: str
    next_human: str
    last_human: str

    @property
    def health(self) -> str:
        # "enabled" is messy for symlinks: we rely on UnitFileState.
        enabled_ok = self.unit_file_state in ("enabled", "static", "linked", "indirect")
        if self.active_state != "active":
            return "BAD"
        if not enabled_ok:
            return "WARN"
        # Timer stays active/running while oneshot curl is in progress — expected.
        if self.sub_state == "running":
            return "OK"
        if self.next_human == "--":
            # Waiting but no next trigger yet (right after install / edge case).
            return "WARN"
        return "OK"


def job_name_from_timer(timer: str) -> str:
    # jacred-job-<name>.timer -> <name>
    if timer.startswith("jacred-job-"):
        timer = timer[len("jacred-job-") :]
    return timer[: -len(".timer")] if timer.endswith(".timer") else timer


def main() -> int:
    require_systemctl()

    timers = list_timer_units()
    if not timers:
        print(f"No jacred-job-*.timer units found in {MANAGED_DIR} (and systemd fallback).", file=sys.stderr)
        return 2

    target_active = systemctl_show(JOBS_TARGET, "ActiveState") or "unknown"
    print("JacRed cron timers check")
    print(f"Target: {JOBS_TARGET} (ActiveState={target_active})")
    print()

    rows: list[JobRow] = []
    for timer in timers:
        job = job_name_from_timer(timer)
        active_state = systemctl_show(timer, "ActiveState") or "unknown"
        sub_state = systemctl_show(timer, "SubState") or ""
        unit_file_state = systemctl_show(timer, "UnitFileState") or "unknown"
        next_us = (
            systemctl_show(timer, "NextElapseUSecRealtime")
            or systemctl_show(timer, "NextElapseUSec")
        )
        last_us = (
            systemctl_show(timer, "LastTriggerUSecRealtime")
            or systemctl_show(timer, "LastTriggerUSec")
        )

        # OnCalendar is sometimes empty via show(); fall back to systemctl cat.
        on_calendar = systemctl_show(timer, "OnCalendar")
        if not on_calendar:
            cat = run(["systemctl", "cat", timer]).stdout or ""
            m = re.search(r"^\s*OnCalendar\s*=\s*(.+?)\s*$", cat, flags=re.M)
            if m:
                on_calendar = m.group(1).strip()

        rows.append(
            JobRow(
                job=job,
                timer=timer,
                active_state=active_state,
                sub_state=sub_state,
                unit_file_state=unit_file_state,
                on_calendar=on_calendar,
                next_human=us_to_local_human(next_us),
                last_human=us_to_local_human(last_us),
            )
        )

    # Sort by health then next time.
    order = {"BAD": 0, "WARN": 1, "OK": 2}
    rows.sort(key=lambda r: (order.get(r.health, 9), r.next_human != "--", r.job))

    # Column widths.
    job_w = max(len(r.job) for r in rows)
    next_w = max(4, min(max(len(r.next_human) for r in rows), 23))
    last_w = max(4, min(max(len(r.last_human) for r in rows), 23))
    oncal_w = min(30, max(10, max(len(r.on_calendar) if r.on_calendar else 2 for r in rows)))

    use_color = sys.stdout.isatty()

    def color(txt: str, code: str) -> str:
        if not use_color:
            return txt
        return f"\033[{code}m{txt}\033[0m"

    def health_color(h: str) -> str:
        return {
            "OK": color(h, "32"),
            "WARN": color(h, "33"),
            "BAD": color(h, "31"),
        }.get(h, h)

    col_tstate_w = 18
    col_file_w = 10
    col_health_w = 5

    def cell(plain: str, disp: str, w: int) -> str:
        return f" {disp}{' ' * max(0, w - len(plain))} "

    border = (
        "+"
        + "-" * (job_w + 2)
        + "+"
        + "-" * (col_tstate_w + 2)
        + "+"
        + "-" * (col_file_w + 2)
        + "+"
        + "-" * (next_w + 2)
        + "+"
        + "-" * (last_w + 2)
        + "+"
        + "-" * (oncal_w + 2)
        + "+"
        + "-" * (col_health_w + 2)
        + "+"
    )

    print(border)
    header_row = (
        "|"
        + cell("JOB", "JOB", job_w)
        + "|"
        + cell("TSTATE", "TSTATE", col_tstate_w)
        + "|"
        + cell("FILE", "FILE", col_file_w)
        + "|"
        + cell("NEXT", "NEXT", next_w)
        + "|"
        + cell("LAST", "LAST", last_w)
        + "|"
        + cell("ONCALENDAR", "ONCALENDAR", oncal_w)
        + "|"
        + cell("HEALTH", "HEALTH", col_health_w)
        + "|"
    )
    print(header_row)
    print(border)

    counts = {"OK": 0, "WARN": 0, "BAD": 0}
    warn_jobs: list[str] = []
    for r in rows:
        counts[r.health] += 1
        if r.health == "WARN":
            warn_jobs.append(r.timer)
        tstate = f"{r.active_state}/{r.sub_state}" if r.sub_state else r.active_state
        oncal = r.on_calendar if r.on_calendar else "--"
        if len(oncal) > oncal_w:
            oncal = oncal[: max(0, oncal_w - 1)] + "…"
        # Avoid ANSI affecting spacing by padding based on plain strings.
        tstate_plain = tstate
        tstate_disp = tstate[: col_tstate_w]
        file_plain = r.unit_file_state
        file_disp = r.unit_file_state[: col_file_w]
        next_plain = r.next_human
        next_disp = r.next_human[:next_w]
        last_plain = r.last_human
        last_disp = r.last_human[:last_w]
        oncal_plain = oncal
        oncal_disp = oncal[:oncal_w]

        health_plain = r.health
        health_disp = health_color(r.health)  # may include ANSI

        row = (
            "|"
            + cell(r.job, r.job[:job_w], job_w)
            + "|"
            + cell(tstate_plain[:col_tstate_w], tstate_disp, col_tstate_w)
            + "|"
            + cell(file_plain[:col_file_w], file_disp, col_file_w)
            + "|"
            + cell(next_plain[:next_w], next_disp, next_w)
            + "|"
            + cell(last_plain[:last_w], last_disp, last_w)
            + "|"
            + cell(oncal_plain[:oncal_w], oncal_disp, oncal_w)
            + "|"
            + cell(health_plain, health_disp, col_health_w)
            + "|"
        )
        print(row)

    print(border)
    print()
    print(f"Summary: OK={counts['OK']} WARN={counts['WARN']} BAD={counts['BAD']}")
    if warn_jobs:
        print()
        print("WARN jobs:")
        for t in warn_jobs:
            svc = t[:-6] + ".service" if t.endswith(".timer") else t
            print(f"  systemctl status {t}")
            print(f"  systemctl status {svc}")
            print(f"  journalctl -u {svc} -n 30 --no-pager")
    if counts["BAD"] > 0:
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

