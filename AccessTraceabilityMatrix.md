# JacRed — Access Traceability Matrix

**Source of truth (code):** `Infrastructure/Security/JacRedEndpointRegistry.cs`  
**Verification:** `JacRedAccessCatalog.VerifyRegistry()` — run at startup (logged on mismatch)  
**Last verified:** 2026-07-09 — all catalog routes match registry

---

## Policy definitions

| Policy | Middleware rule | Keys |
|--------|-----------------|------|
| **Public** | Always allow | — |
| **ConfigApi** | LAN client **OR** valid devkey (same-host proxy alone **not** enough) | `X-Dev-Key`, `?devkey=` |
| **DevAdmin** | LAN client **OR** valid devkey (same-host proxy alone **not** enough) | `X-Dev-Key`, `?devkey=` |
| **ApiKeyWhenConfigured** | Allow if `apikey` unset in config; else require valid key | `?apikey=`, `X-Api-Key`, `Bearer` |

**Deny codes:** OPTIONS → 204; key configured → 401; else 403.

**Network context:** Client IP = after `X-Forwarded-For`; Peer IP = direct TCP (see `ClientNetworkContext`).

---

## Path prefix → policy (registry)

| Path prefix / pattern | Policy | Notes |
|----------------------|--------|-------|
| `/dev/` | DevAdmin | All dev maintenance/diagnostics |
| `/cron/` | DevAdmin | Tracker sync triggers |
| `/jsondb`, `/jsondb/` | DevAdmin | FileDB admin |
| `/api/v1.0/config` | ConfigApi | Settings API (secrets in response) |
| `/`, `/stats`, `/settings` | Public | HTML shells only |
| `/health`, `/version`, `/lastupdatedb` | Public | Health probes |
| `/api/v1.0/conf` | Public | Jackett apikey probe |
| `/sync/` | Public | Middleware open; `opensync` in SyncController |
| `/swagger`, `/openapi.yaml` | Public | API docs |
| `/css/`, `/js/`, `/img/`, `/vendor/`, `/fonts/` | Public | Static assets (when `web=true`) |
| `/opensearch.xml`, `/manifest.json`, `/sw.js` | Public | PWA metadata |
| *everything else* | ApiKeyWhenConfigured | Search, stats JSON, torznab, jackett |

---

## Endpoint traceability (controller → policy)

### Public

| Route | Controller | Secondary gate |
|-------|------------|----------------|
| `GET /` | HomeController | — |
| `GET /stats` | HomeController | HTML shell (JSON at `/stats/*` is not public) |
| `GET /settings` | HomeController | HTML shell |
| `GET /opensearch.xml` | HomeController | — |
| `GET /health` | HealthController | — |
| `GET /version` | HealthController | — |
| `GET /lastupdatedb` | HealthController | — |
| `GET /api/v1.0/conf` | HealthController | Returns apikey validity hint |
| `GET /sync/conf` | SyncController | — |
| `GET /sync/fdb` | SyncController | `opensync` |
| `GET /sync/fdb/torrents` | SyncController | `opensync` |
| `GET /sync/torrents` | SyncController | deprecated — returns v1 removed error |
| `GET /sync/tracks/stats` | SyncController | `opensync` |
| `GET /swagger`, `/openapi.yaml` | Startup / Swagger | — |

### ConfigApi

| Route | Controller |
|-------|------------|
| `GET/POST /api/v1.0/config` | ConfigController |
| `GET /api/v1.0/config/schema` | ConfigController |
| `POST /api/v1.0/config/validate` | ConfigController |
| `POST /api/v1.0/config/diff` | ConfigController |
| `POST /api/v1.0/config/render` | ConfigController |
| `POST /api/v1.0/config/parse` | ConfigController |
| `POST /api/v1.0/config/format` | ConfigController |

### DevAdmin

| Route pattern | Controller |
|---------------|------------|
| `/dev/*` | DevMaintenanceController, DevDiagnosticsController, DevMigrationController, DevTracksController |
| `/jsondb/*` | DbController |
| `/cron/{tracker}/*` | Controllers/Cron/* (17 trackers) |

### ApiKeyWhenConfigured

| Route | Controller | Secondary gate |
|-------|------------|----------------|
| `GET /api/v1.0/torrents` | TorrentsController | — |
| `GET /api/v1.0/qualitys` | TorrentsController | — |
| `GET /api/v2.0/indexers/{status}/results` | JackettController | — |
| `GET /torznab/api` | TorznabController | — |
| `GET /api/v2.0/indexers/{indexer}/results/torznab/api` | TorznabController | — |
| `GET /api/v1/indexer/{indexer}/newznab` | TorznabController | — |
| `GET /api/v2.0/indexers` | TorznabController | — |
| `GET /api/v1/indexer` | TorznabController | — |
| `GET /api/v1/indexer/{id}` | TorznabController | — |
| `GET /stats/trackers` | StatsController | `openstats` |
| `GET /stats/meta` | StatsController | `openstats` |
| `GET /stats/torrents` | StatsController | `openstats` |
| `GET /stats/trackers/{name}/new` | StatsController | `openstats` |
| `GET /stats/trackers/{name}/updated` | StatsController | `openstats` |

---

## Access by client context

| Policy | Loopback / LAN | Same-host proxy (no devkey) | Remote / tunnel |
|--------|----------------|----------------------------|-----------------|
| Public | ✓ | ✓ | ✓ |
| ConfigApi | ✓ | ✗ | devkey required |
| DevAdmin | ✓ | ✗ | devkey required if set |
| ApiKeyWhenConfigured | apikey if configured | apikey if configured | apikey if configured |

---

## Registry verification result

All routes in `JacRedAccessCatalog.Routes` were checked against `JacRedEndpointRegistry.ResolvePolicy()` — **0 mismatches** (verified at build/startup).

To re-check after changes:

```csharp
var errors = JacRedAccessCatalog.VerifyRegistry();
```

---

## Removed legacy (Phase S cleanup)

- `ModHeaders` middleware facade — replaced by `UseJacRedSecurity()`
- `LocalhostOnlyAttribute` — LAN now handled by DevAdmin policy
- `[JacRedAuthorize]` attributes — unused; registry is single source of truth
- Duplicate apikey extraction in HealthController — uses `JacRedKeyUtils`
- `Engine/**` excluded from compile (stale pre-refactor tree)

---

## Operational logging (journalctl)

Console output uses grep-friendly category prefixes (`tracks:`, `sync:`, `sync_spidr:`, `cron:`, `fdb:`, …). **File logs** under `Data/log/` are **on by default** (`logFdb`, `logParsers`, `trackslog`). Optional console tuning in `init.yaml`:

```yaml
logging:
  defaultLevel: Information
  consoleTimestamp: false
  tracksConsoleDetail: false   # compact tracks console (failures only)
  cronSkipFastMs: 100          # sub-100ms HTTP 200 /cron/ → Debug
  categories:
    tracks: Warning
    fdb: Warning
    parsers: None              # file-only (ParserLog)
```

```bash
journalctl -u jacred -g 'sync_spidr:'
journalctl -u jacred -g 'cron:' -p warning
journalctl -u jacred -g 'tracks:' -p warning
journalctl -u jacred -g 'fdb:' -p warning
```
