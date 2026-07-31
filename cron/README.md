# JacRed HTTP job scheduler

Replaces host **crontab** with **systemd timers** — one timer per job, no long-running scheduler daemon.

- Config: [`jobs.yaml`](jobs.yaml) (YAML, cron schedules + paths)
- Generator: [`generate.py`](generate.py) → `generated/*.service` + `*.timer`
- Install: [`install.sh`](install.sh)
- Runner: [`run-job.sh`](run-job.sh) (curl oneshot per job)

## Why systemd timers

| crontab / bash loop | systemd timers |
|---------------------|----------------|
| Stuck `curl` processes pile up | Each job = separate oneshot |
| One daemon or many crontab lines | Native `systemctl list-timers` |
| Hard to disable one tracker | `systemctl disable jacred-job-rutor-parse.timer` |

Trackers run **in parallel** (independent timers). JacRed returns `ok` / `work` / `disabled` immediately when a tracker is busy (`TrackerParseLock` in app code).

## Install

Assumes JacRed at `/opt/jacred` (adjust paths if your install differs).

```bash
chmod +x /opt/jacred/cron/install.sh /opt/jacred/cron/run-job.sh
sudo /opt/jacred/cron/install.sh
```

This generates units into `/etc/systemd/jacred-cron/`, creates symlinks in `/etc/systemd/system/`, enables `jacred-jobs.target`, and disables legacy `jacred-scheduler.service` if present.

Use custom paths if needed:

```bash
sudo MANAGED_DIR=/etc/systemd/jacred-cron SYSTEMD_DIR=/etc/systemd/system /opt/jacred/cron/install.sh
```

### Migrate off crontab

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
  - name: rutor-UpdateTasksParse
    schedule: "* */4 * * *"
    path: /cron/rutor/UpdateTasksParse
    enabled: false   # optional — skip this job
```

| Field | Meaning |
|-------|---------|
| `base_url` | JacRed HTTP base (change port/host here) |
| `schedule` | Standard 5-field cron (same as `Data/crontab`) |
| `path` | URL path relative to `base_url` |
| `enabled` | `false` to disable a job |

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
| `ok` | Job finished |
| `work` | Tracker already busy |
| `disabled` | Tracker disabled in config |

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

This script prints a table for all `jacred-job-*.timer` units and highlights anything not `active/enabled`.

```bash
/opt/jacred/cron/check-jobs.sh
```

### Restart all timers / stop stuck jobs

Stops running oneshot services (long `curl`s) and restarts every `jacred-job-*.timer`:

```bash
sudo /opt/jacred/cron/restart-jobs.sh
```

`WARN` / long-running jobs: a timer in `active/running` while the oneshot `curl` is still waiting for JacRed (`ParseAllTask`, `UpdateTasksParse`) is **normal**. Check the matching `.service` + journal if a job looks stuck for hours.

### Schedule notes (avoid over-firing)

| Pattern | Meaning | Prefer |
|---------|---------|--------|
| `* */4 * * *` | **every minute** in hours 0/4/8/12/16/20 | `5 */4 * * *` (once per window) |
| `*/5` ParseAllTask | every 5 min while a multi-hour crawl holds HTTP | hourly `30 * * * *` |

If you keep systemd units in a different managed folder:

```bash
MANAGED_DIR=/etc/systemd/jacred-cron /opt/jacred/cron/check-jobs.sh
```

## Layout

```text
cron/
  jobs.yaml         # edit schedules here
  generate.py       # cron → systemd OnCalendar
  run-job.sh        # curl runner (oneshot)
  install.sh        # generate + enable timers
  restart-jobs.sh   # stop oneshots + restart all timers
  check-jobs.sh     # status table (wraps check-jobs.py)
  generated/        # output (gitignored; created on install)
  README.md
```
