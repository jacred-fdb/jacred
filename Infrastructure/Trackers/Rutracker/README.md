# Rutracker tracker

JacRed sync for [rutracker.org](https://rutracker.org) via cron endpoints under `/cron/rutracker/`.

## Cloudflare / FlareSolverr

Rutracker sits behind Cloudflare (`403` / `cf-mitigated` / “Just a moment…”). Direct .NET `HttpClient` cannot reuse `cf_clearance` cookies (TLS fingerprint differs). Production path:

1. **FlareSolverr** (primary) — persistent browser session; guarded hosts are fetched entirely through the browser (~80 s first page, then 2–3 s).
2. **Worker `alias`** (optional fallback when FlareSolverr is disabled) — reverse-proxy so fetches go through CF edge.

```yaml
flaresolverr:
  enable: true
  url: http://127.0.0.1:8191/v1   # compose: http://flaresolverr:8191/v1
  maxTimeoutMs: 180000
  sessionIdleMinutes: 30
  guardedHours: 6
  recheckMinutes: 30

Rutracker:
  host: https://rutracker.org
  alias: ""          # optional Worker URL if flaresolverr.enable=false
```

- **HTTP** (lists, topics): `HttpClient.Get` → auto-routes to FlareSolverr when the host is guarded; `rqHost()` still applies alias when set  
- **FDB `url`**: always canonical `https://rutracker.org/forum/viewtopic.php?t=…`  
- **Login**: not required for current parser (magnets on public topic pages; auth code is commented out)

Warm the browser session before parse (first solve is expensive):

```cron
55 * * * *  /opt/jacred/Data/run-job.sh cloudflare-warmup http://127.0.0.1:9117/cron/cloudflare/Warmup 180
0 * * * *   /opt/jacred/Data/run-job.sh rutracker-parse http://127.0.0.1:9117/cron/rutracker/parse 900
```

Limited smoke (after app + FlareSolverr are up):

```bash
./scripts/cron_rutracker_smoke.sh
# or: curl 'http://127.0.0.1:9117/cron/rutracker/parse?page=0&cat=2090&maxTopics=3'
```

## Cron endpoints

| Action | Code path | What it does |
| -------- | ----------- | -------------- |
| `Warmup` | `/cron/cloudflare/Warmup` | FlareSolverr session warm (default forum `f=2090`) |
| `Parse` | `ParseAsync` | First page (`page=0` by default) of each **QuickParse** forum (~**65**); optional `cat`, `maxTopics` for smoke |
| `UpdateTasksParse` | `UpdateTasksParseAsync` | Hits **all ~211** forums to learn page counts → `Data/temp/rutracker_taskParse.json` (**no lock**) |
| `ParseLatest` | `ParseLatestAsync` | First *N* pages of **every** cat in `taskParse` (heavy once the map is full) |
| `ParseAllTask` | `ParseAllTaskAsync` | Full backlog of every page in `taskParse` (multi-hour) |

### Request unit (`parsePage`)

1. **1×** GET forum list  
2. For each torrent **not** already in FDB with the same title: **1×** GET topic (magnet)

`parseDelay` / `reqMinute` apply to `ParseLatest` and `ParseAllTask` only — **not** to `Parse` or `UpdateTasksParse`.

Category counts (from `RutrackerCategories`): **211** forums, **65** `QuickParse = true`.

## Recommended cron (minimize requests, keep useful freshness)

Primary freshness is **`Parse` page 0 of QuickParse**, not `ParseAllTask`.

Repo [`Data/crontab`](../../../Data/crontab) follows this cadence (ParseAll twice daily so a 6h wall can continue same day):

```cron
# Warm FlareSolverr ~5 min before parse
55 * * * *    /opt/jacred/Data/run-job.sh cloudflare-warmup http://127.0.0.1:9117/cron/cloudflare/Warmup 180

# Fresh releases: 65 quick forums, first page only (~65 GETs/run)
0 * * * *     /opt/jacred/Data/run-job.sh rutracker-parse http://127.0.0.1:9117/cron/rutracker/parse 900

# Rebuild page-task map once (211 GETs) — not every few hours
20 3 * * *    /opt/jacred/Data/run-job.sh rutracker-UpdateTasksParse http://127.0.0.1:9117/cron/rutracker/UpdateTasksParse 60

# Deep crawl: morning start + ~6h later continue (pages with updateTime != today)
40 4,11 * * * /opt/jacred/Data/run-job.sh rutracker-ParseAllTask http://127.0.0.1:9117/cron/rutracker/ParseAllTask 60
```

### Avoid

- Hourly `UpdateTasksParse` / `ParseAllTask` — over-requests; while a crawl runs cron only gets `work`. Prefer 2×/day ParseAll (start + continue).  
- Scheduling `ParseLatest?pages=5` for “light” refresh — with a full `taskParse` it is **heavier** than hourly `parse` (~211×N forum pages).
- Skipping warmup when FlareSolverr is cold — first CF solve under CPU contention often times out.

### Tunable intensity

| Goal | Change |
| ------ | -------- |
| Fresher (~30 min) | `*/30 * * * *` → `parse` |
| Quieter | `0 */2 * * *` → `parse` |
| Slower deep crawl | raise `Rutracker.reqMinute` (longer `parseDelay`) |

## FlareSolverr / Worker requests / day (recommended cron)

Assumptions: 65 QuickParse, 211 forums, ~**40** pages/cat average for full crawl; topic GETs only when FDB misses title. With FlareSolverr each GET is a browser navigation (serialized).

### Fixed forum GETs

| Component | Forum GETs |
| ----------- | ------------ |
| `parse` hourly | **1 560 / day** (65 × 24) |
| `UpdateTasksParse` daily | **211 / day** |
| `ParseAllTask` 2×/day (up to 6h each) | forum GETs depend on unfinished pages; floor often **~1–2k / day** amortized when warm |
| **Forum floor** | **~2–4 000 / day** (parse + Update + partial ParseAll) |

### Totals including topic/magnet GETs (warm DB)

| Horizon | Forum only | Quiet (~300 topic/day) | Typical (~750 topic/day) | Busy (~1 500 topic/day) |
| --------- | ------------ | ------------------------ | -------------------------- | ------------------------- |
| **1 day** | ~3 000 | ~3 300 | ~3 800 | ~4 500 |
| **7 days** | ~21 000 | ~23 000 | ~27 000 | ~32 000 |
| **30 days** | ~89 000 | ~100 000 | ~114 000 | ~136 000 |

Planning ballpark: **~3.5k–4.5k / day**, **~25k–30k / week**, **~100k–120k / month**.

A cold first full crawl can spike topic GETs for several days until the task map pages catch up.

### vs aggressive schedules

Forum floor alone is often **~15k+ / day** if Update/ParseAll fire hourly; prefer the balanced block above (also the repo default in `Data/crontab`).

## Related files

- `RutrackerSyncService.cs` — cron sync  
- `RutrackerParser.cs` — HTML → torrents / magnets  
- `RutrackerCategories.cs` — forum map + QuickParse  
- `Controllers/Cron/RutrackerController.cs` — HTTP entrypoints  
- `Controllers/Cron/CloudflareController.cs` — FlareSolverr warmup  
- `Infrastructure/Networking/CloudflareClearance.cs` — FlareSolverr client  
- `scripts/cron_rutracker_smoke.sh` — limited live smoke  
- Repo `Data/crontab` / `Data/run-job.sh` — balanced schedule (matches recommended block above)
