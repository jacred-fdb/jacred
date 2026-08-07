# JacRed HTTP job scheduler

Replaces host **crontab** with **systemd timers** — one timer per job, no long-running scheduler daemon.

- Config: [`jobs.yaml`](jobs.yaml) (YAML, cron schedules + paths + `max_time`)
- Generator: [`generate.py`](generate.py) → `generated/*.service` + `*.timer` + `*.env`
- Install: [`install.sh`](install.sh)
- Runner: [`run-job.sh`](run-job.sh) (curl oneshot per job, with `--max-time`)

## Why systemd timers

| crontab / bash loop | systemd timers |
|---------------------|----------------|
| Stuck `curl` processes pile up | Each job = separate oneshot + flock |
| One daemon or many crontab lines | Native `systemctl list-timers` |
| Hard to disable one tracker | `systemctl disable jacred-job-rutor-parse.timer` |
| No request deadline | `max_time` → curl `--max-time` + finite `TimeoutStartSec` |

Trackers run **in parallel** (independent timers). JacRed returns `ok` / `work` / `disabled` quickly when a tracker is busy. Long jobs (`ParseAllTask`, `UpdateTasksParse`) start work in the background and return immediately; curl still has a hard deadline.

## Install

Assumes JacRed at `/opt/jacred` (adjust paths if your install differs).

```bash
chmod +x /opt/jacred/cron/install.sh /opt/jacred/cron/run-job.sh
sudo /opt/jacred/cron/install.sh
```

This generates units into `/etc/systemd/jacred-cron/`, creates symlinks in `/etc/systemd/system/`, enables `jacred-jobs.target`, and disables legacy `jacred-scheduler.service` if present. It also warns if host crontab still curls `127.0.0.1:9117`.

Use custom paths if needed:

```bash
sudo MANAGED_DIR=/etc/systemd/jacred-cron SYSTEMD_DIR=/etc/systemd/system /opt/jacred/cron/install.sh
```

### Migrate off crontab

**Required** — leftover crontab entries have no flock and will pile up curls next to systemd:

```bash
crontab -l | grep -vF '127.0.0.1:9117' | crontab -
```

`Data/crontab` remains in the repo as a legacy reference.

## Manage jobs

Edit [`jobs.yaml`](jobs.yaml):

```yaml
base_url: http://127.0.0.1:9117

jobs:
  - name: rutor-parse
    schedule: "*/15 * * * *"
    path: /cron/rutor/parse
    max_time: 900
  - name: rutor-UpdateTasksParse
    schedule: "5 */4 * * *"
    path: /cron/rutor/UpdateTasksParse
    max_time: 60
    enabled: false   # optional — skip this job
```

| Field | Meaning |
|-------|---------|
| `base_url` | JacRed HTTP base (change port/host here) |
| `schedule` | Standard 5-field cron (same as `Data/crontab`) |
| `path` | URL path relative to `base_url` |
| `max_time` | Curl overall timeout seconds (also drives systemd `TimeoutStartSec`). For ack jobs this is only the HTTP response deadline — not the crawl duration. |
| `enabled` | `false` to disable a job |

Defaults when `max_time` is omitted: ack jobs (`ParseAllTask`, `UpdateTasksParse`, `jsondb/save`) → **60s**; everything else → 900 (15m).

Long crawls still run in-process with wall-clock limits (ParseAll ≈ 6h, UpdateTasks ≈ 30m). Curl `max_time` must stay short so a hung ack is detected quickly.

After changes:

```bash
sudo /opt/jacred/cron/install.sh
```

If `apikey` / `devkey` is required, add query params to `path` (e.g. `/cron/rutor/parse?devkey=...`).

## Status and logs

```bash
systemctl list-timers 'jacred-job-*'
systemctl status jacred-jobs.target
journalctl -u 'jacred-job-rutor-parse.service' -f
```

Disable one job:

```bash
sudo systemctl disable --now jacred-job-rutor-parse.timer
```

Run one job manually:

```bash
sudo systemctl start jacred-job-rutor-parse.service
```

## JacRed responses

| Body | Meaning |
|------|---------|
| `ok` | Job finished (or long job started in background) |
| `work` | Tracker already busy |
| `disabled` | Tracker disabled in config |
| `TIMEOUT …` | Curl hit `max_time` (oneshot fails; see journal) |

## Regenerate without full install

```bash
cd /opt/jacred/cron
python3 generate.py
sudo mkdir -p /etc/systemd/jacred-cron
sudo cp generated/*.service generated/*.timer generated/jacred-jobs.target /etc/systemd/jacred-cron/
for f in /etc/systemd/system/jacred-job-*.service /etc/systemd/system/jacred-job-*.timer /etc/systemd/system/jacred-jobs.target; do
  [ -e "$f" ] || [ -L "$f" ] && sudo rm -f "$f"
done
for f in /etc/systemd/jacred-cron/jacred-job-*.service /etc/systemd/jacred-cron/jacred-job-*.timer; do
  sudo ln -sfn "$f" "/etc/systemd/system/$(basename "$f")"
done
sudo ln -sfn /etc/systemd/jacred-cron/jacred-jobs.target /etc/systemd/system/jacred-jobs.target
sudo systemctl daemon-reload
sudo systemctl restart jacred-jobs.target
```

## Check timers (recommended)

This script prints a table for all `jacred-job-*.timer` units and highlights anything not `active/enabled`. Onesots still running past `MAX_TIME` are marked **STUCK**.

```bash
/opt/jacred/cron/check-jobs.sh
# Stop oneshots that exceeded MAX_TIME:
/opt/jacred/cron/check-jobs.sh --fix-stuck
```

After install, ack oneshots (`ParseAllTask`, `UpdateTasksParse`, `jsondb/save`) should finish in a few seconds. If a service stays activating past `max_time` (default 60s), it is marked **STUCK**.

### Restart all timers / stop stuck jobs

Stops running oneshot services (timed-out or stuck `curl`s) and restarts every `jacred-job-*.timer`:

```bash
sudo /opt/jacred/cron/restart-jobs.sh
```

With `max_time` + finite `TimeoutStartSec`, systemd/curl should clear hung oneshots without manual restarts. Use `restart-jobs.sh` or `check-jobs.sh --fix-stuck` if a unit is marked STUCK.

### Schedule notes (avoid over-firing)

| Pattern | Meaning | Prefer |
|---------|---------|--------|
| `* */4 * * *` | **every minute** in hours 0/4/8/12/16/20 | `5 */4 * * *` (once per window) |
| `*/5` ParseAllTask | every 5 min while crawl holds HTTP (legacy sync) | hourly `30 * * * *` (ack is fast; schedule for crawl cadence) |

If you keep systemd units in a different managed folder:

```bash
MANAGED_DIR=/etc/systemd/jacred-cron /opt/jacred/cron/check-jobs.sh
```

## Layout

```text
cron/
  jobs.yaml         # edit schedules + max_time here
  generate.py       # cron → systemd OnCalendar + MAX_TIME env
  run-job.sh        # curl runner (oneshot, --max-time)
  install.sh        # generate + enable timers (+ crontab warn)
  restart-jobs.sh   # stop oneshots + restart all timers
  check-jobs.sh     # status table (wraps check-jobs.py)
  generated/        # output (gitignored; created on install)
  README.md
```
