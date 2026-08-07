#!/usr/bin/env python3
"""Generate cron/generated/*.env + safe crontab from cron/jobs.yaml (stdlib only)."""

import argparse
import os
import re
import sys
from pathlib import Path

# Defaults when jobs.yaml omits max_time (seconds).
# Ack jobs return immediately (work runs in-app); curl only needs a short deadline.
DEFAULT_MAX_TIME_PARSE = 900
DEFAULT_MAX_TIME_ACK = 60


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
        elif key == "max_time":
            current[key] = int(val)
        else:
            current[key] = val

    if current:
        jobs.append(current)

    if not base_url:
        raise ValueError("base_url is required in jobs.yaml")
    return base_url, jobs


def default_max_time(path_suffix: str) -> int:
    lower = path_suffix.lower()
    # Background-ack endpoints: HTTP returns ok/work/disabled immediately.
    if "parsealltask" in lower or "updatetasksparse" in lower or "jsondb/save" in lower or lower.endswith("/save"):
        return DEFAULT_MAX_TIME_ACK
    return DEFAULT_MAX_TIME_PARSE


def resolve_max_time(job: dict) -> int:
    if "max_time" in job:
        return int(job["max_time"])
    return default_max_time(str(job.get("path", "")))


def env_name(job_name: str) -> str:
    return f"jacred-job-{job_name}"


def write_env(path: Path, job_name: str, job_url: str, max_time: int) -> None:
    path.write_text(
        f"JOB_NAME={job_name}\nJOB_URL={job_url}\nMAX_TIME={max_time}\n",
        encoding="utf-8",
    )


def write_crontab(path: Path, run_job: Path, jobs: list[dict]) -> int:
    """Write host crontab lines that invoke run-job.sh (flock + --max-time)."""
    lines = [
        "# JacRed safe crontab — GENERATED from cron/jobs.yaml (do not edit by hand).",
        "# Regenerate: python3 /opt/jacred/cron/generate.py",
        "# Install:    sudo /opt/jacred/cron/install.sh",
        "#             (or: crontab /opt/jacred/Data/crontab)",
        "#",
        "# Uses run-job.sh: flock (no curl pile-up) + curl --max-time from generated .env.",
        "# First run generate.py / install.sh so cron/generated/*.env exist.",
        "# Override install path: JACRED_CRON_DIR=/path/to/cron python3 generate.py",
        "#",
        "SHELL=/bin/bash",
        "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
        "",
    ]
    count = 0
    for job in jobs:
        name = job.get("name")
        if not name or not job.get("enabled", True):
            continue
        schedule = job.get("schedule")
        path_suffix = job.get("path")
        if not schedule or not path_suffix:
            continue
        max_time = resolve_max_time(job)
        lines.append(f"# {name} -> {path_suffix} (max_time={max_time}s)")
        lines.append(f"{schedule}  {run_job} {name}")
        lines.append("")
        count += 1

    path.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")
    return count


def generate(cron_dir: Path) -> int:
    yaml_path = cron_dir / "jobs.yaml"
    out_dir = cron_dir / "generated"
    out_dir.mkdir(parents=True, exist_ok=True)

    # Clear old generated artifacts (env, crontab, leftover systemd units).
    for old in out_dir.glob("jacred-*"):
        old.unlink()
    for stale in ("crontab",):
        p = out_dir / stale
        if p.is_file():
            p.unlink()
    for pattern in ("*.service", "*.timer", "jacred-jobs.target"):
        for old in out_dir.glob(pattern):
            old.unlink()

    base_url, jobs = parse_jobs_yaml(yaml_path)
    base_url = base_url.rstrip("/")
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

        unit = env_name(name)
        job_url = f"{base_url}/{path_suffix.lstrip('/')}"
        max_time = resolve_max_time(job)
        if max_time <= 0:
            raise ValueError(f"job {name}: max_time must be > 0")

        write_env(out_dir / f"{unit}.env", name, job_url, max_time)
        enabled_count += 1
        print(f"  {name}: {schedule} -> {path_suffix} (max_time={max_time}s)")

    install_cron = Path(os.environ.get("JACRED_CRON_DIR", "/opt/jacred/cron")).resolve()
    run_job = install_cron / "run-job.sh"
    crontab_path = out_dir / "crontab"
    write_crontab(crontab_path, run_job, jobs)
    data_crontab = cron_dir.parent / "Data" / "crontab"
    if data_crontab.parent.is_dir():
        write_crontab(data_crontab, run_job, jobs)
        print(f"wrote Data/crontab ({enabled_count} jobs) run_job={run_job}")
    print(f"wrote {crontab_path} run_job={run_job}")
    print(f"generated {enabled_count} jobs in {out_dir}")
    return enabled_count


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate run-job .env files + safe crontab from jobs.yaml"
    )
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
