#!/usr/bin/env python3
"""Generate systemd service/timer units from cron/jobs.yaml (stdlib only)."""

import argparse
import re
import sys
from pathlib import Path


def parse_jobs_yaml(path: Path) -> tuple[str, list[dict]]:
    """Minimal YAML parser for jobs.yaml schema (no PyYAML)."""
    base_url = ""
    jobs: list[dict] = []
    current: dict | None = None
    in_jobs = False

    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.split("#", 1)[0].rstrip()
        if not line.strip():
            continue

        if re.match(r"^base_url\s*:", line):
            base_url = line.split(":", 1)[1].strip().strip('"').strip("'")
            continue

        if re.match(r"^jobs\s*:", line):
            in_jobs = True
            continue

        if not in_jobs:
            continue

        if re.match(r"^\s*-\s+name\s*:", line):
            if current:
                jobs.append(current)
            name = line.split(":", 1)[1].strip().strip('"').strip("'")
            current = {"name": name, "enabled": True}
            continue

        if current is None:
            continue

        m = re.match(r"^\s+(\w+)\s*:\s*(.+)$", line)
        if not m:
            continue
        key, val = m.group(1), m.group(2).strip().strip('"').strip("'")
        if key == "enabled":
            current[key] = val.lower() in ("true", "yes", "1")
        else:
            current[key] = val

    if current:
        jobs.append(current)

    if not base_url:
        raise ValueError("base_url is required in jobs.yaml")
    return base_url, jobs


def expand_step_field(field: str, upper: int) -> str:
    if field == "*":
        return "*"
    if field.startswith("*/"):
        step = int(field[2:])
        return ",".join(str(i) for i in range(0, upper, step))
    return field


def cron_minute_field(minute: str) -> str:
    if minute == "*":
        return "*"
    if minute.startswith("*/"):
        return f"0/{minute[2:]}"
    return minute


def cron_dow_field(dow: str) -> str:
    if dow == "*":
        return "*"

    dow_map = {
        "0": "Sun",
        "1": "Mon",
        "2": "Tue",
        "3": "Wed",
        "4": "Thu",
        "5": "Fri",
        "6": "Sat",
        "7": "Sun",
    }

    parts = [p.strip() for p in dow.split(",")]
    names: list[str] = []
    for part in parts:
        if part not in dow_map:
            raise ValueError(f"dow field not supported: {dow!r}")
        names.append(dow_map[part])
    return ",".join(names)


def cron_to_on_calendar(schedule: str) -> str:
    parts = schedule.split()
    if len(parts) < 5:
        raise ValueError(f"invalid cron schedule: {schedule!r}")

    minute, hour, day, month, dow = parts[:5]
    if day != "*" or month != "*":
        raise ValueError(f"day/month fields not supported yet: {schedule!r}")

    hour_str = expand_step_field(hour, 24)
    min_str = cron_minute_field(minute)
    dow_str = cron_dow_field(dow)

    date_part = "*-*-*"
    if dow_str != "*":
        date_part = f"{dow_str} {date_part}"

    if hour_str == "*" and min_str == "*":
        return f"{date_part} *:*:00"
    if hour_str == "*" and min_str.startswith("0/"):
        return f"{date_part} *:{min_str}:00"
    if hour_str == "*" and min_str != "*":
        return f"{date_part} *:{min_str}:00"
    if hour_str != "*" and min_str == "*":
        return f"{date_part} {hour_str}:*:00"
    return f"{date_part} {hour_str}:{min_str}:00"


def unit_name(job_name: str) -> str:
    return f"jacred-job-{job_name}"


def write_env(path: Path, job_name: str, job_url: str) -> None:
    path.write_text(
        f"JOB_NAME={job_name}\nJOB_URL={job_url}\n",
        encoding="utf-8",
    )


def write_service(path: Path, unit: str, cron_dir: Path, job_name: str) -> None:
    run_job = cron_dir / "run-job.sh"
    content = f"""[Unit]
Description=JacRed HTTP job {job_name}

[Service]
Type=oneshot
# Long ParseAllTask / UpdateTasksParse hold curl until JacRed responds.
TimeoutStartSec=infinity
ExecStart=/bin/bash {run_job} {job_name}
"""
    path.write_text(content, encoding="utf-8")


def write_timer(path: Path, unit: str, on_calendar: str) -> None:
    content = f"""[Unit]
Description=Timer for JacRed job {unit}

[Timer]
OnCalendar={on_calendar}
# Avoid reboot catch-up stampede of missed heavy jobs.
Persistent=false
Unit={unit}.service

[Install]
WantedBy=jacred-jobs.target
"""
    path.write_text(content, encoding="utf-8")


def write_target(path: Path, timer_units: list[str]) -> None:
    wants = "\n".join(f"Wants={u}.timer" for u in timer_units)
    content = f"""[Unit]
Description=All JacRed HTTP job timers
{wants}

[Install]
WantedBy=multi-user.target
"""
    path.write_text(content, encoding="utf-8")


def generate(cron_dir: Path) -> int:
    yaml_path = cron_dir / "jobs.yaml"
    out_dir = cron_dir / "generated"
    out_dir.mkdir(parents=True, exist_ok=True)

    # Clear old generated units (keep .gitignore)
    for old in out_dir.glob("jacred-*"):
        old.unlink()

    base_url, jobs = parse_jobs_yaml(yaml_path)
    base_url = base_url.rstrip("/")
    timer_units: list[str] = []
    enabled_count = 0

    for job in jobs:
        name = job.get("name")
        if not name:
            continue
        if not job.get("enabled", True):
            print(f"skip disabled: {name}")
            continue
        schedule = job.get("schedule")
        path_suffix = job.get("path")
        if not schedule or not path_suffix:
            raise ValueError(f"job {name}: schedule and path are required")

        on_calendar = cron_to_on_calendar(schedule)
        unit = unit_name(name)
        job_url = f"{base_url}/{path_suffix.lstrip('/')}"

        write_env(out_dir / f"{unit}.env", name, job_url)
        write_service(out_dir / f"{unit}.service", unit, cron_dir.resolve(), name)
        write_timer(out_dir / f"{unit}.timer", unit, on_calendar)

        timer_units.append(unit)
        enabled_count += 1
        print(f"  {unit}: {on_calendar} -> {path_suffix}")

    write_target(out_dir / "jacred-jobs.target", timer_units)
    print(f"generated {enabled_count} jobs in {out_dir}")
    return enabled_count


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate systemd units from jobs.yaml")
    parser.add_argument(
        "--cron-dir",
        type=Path,
        default=Path(__file__).resolve().parent,
        help="cron directory containing jobs.yaml",
    )
    args = parser.parse_args()
    try:
        count = generate(args.cron_dir.resolve())
        if count == 0:
            print("warning: no enabled jobs", file=sys.stderr)
    except (ValueError, OSError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
