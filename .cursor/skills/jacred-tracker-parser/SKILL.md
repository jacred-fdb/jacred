---
name: jacred-tracker-parser
description: >-
  Adds and debugs JacRed torrent/anime tracker parsers (site probe, auth, magnet
  policy, SyncService, cron, fixtures, dry-run, OpenAPI wiring). Use when adding
  a tracker, scraping a listing/API, quality gates (1080-only), Batch packs,
  FlareSolverr, crontab, or when the user mentions tracker slug, ParseShows,
  limit_page, MagnetNoTrackers, or Jackett defs.
---

# JacRed tracker parser

Project playbook for integrating a real tracker into JacRed. Pair with the
specialist subagent [`.cursor/agents/jacred-tracker-parser.md`](../../agents/jacred-tracker-parser.md).
Full slug catalog: [reference.md](reference.md).

## Workflow

Copy and track:

```
Tracker Progress:
- [ ] 1. Probe site (encoding, auth, API vs HTML, pagination, download path)
- [ ] 2. Choose sync cluster + magnet/auth policy (see reference.md)
- [ ] 3. Implement Parser + SyncService + Controller
- [ ] 4. Wire DI/config/schema/FileDB/crontab/OpenAPI/docs/icon
- [ ] 5. Fixtures + dry_run_{slug}_parser.py + fixture tests
- [ ] 6. Run dry-run + dotnet test --filter FullyQualifiedName~{Name}
```

Do not ship half-wired trackers. Do not edit the user’s `.cursor/plans/*.plan.md`
unless asked.

## Site probe (before code)

1. Fetch listing/home + one detail/show page (and site JS if downloads are AJAX).
2. Check Jackett def if any; **verify live** (paths drift).
3. Record: encoding, auth, magnet vs `.torrent`, quality ladder, pagination, host mirrors.
4. Passkey risk? Prefer `BencodeTo.MagnetNoTrackers`.
5. Multi-torrent page? Stable unique FDB `url` (`?ep=` / `?id=` / `#quality`).

Prefer official JSON when the site’s own JS uses it (SubsPlease/Knaben/Bitru/Aniliberty).

## Sync cluster (pick closest clone)

| Cluster | Clone from | Cron shape |
|---------|------------|------------|
| Rutor trio (+ ParseLatest) | rutor, korsars, anibelka, ultradox, rutracker | Hourly parse + daily tasks |
| Page-range / `limit_page` | baibako, rudub, anidub, anistar, leproduction | `parseFrom`/`parseTo` or `limit_page` |
| API latest + backfill | knaben, bitru, subsplease | Hourly latest + checkpointed crawl |
| Special | lostfilm, mazepa | Dedicated endpoints |

Details per slug: [reference.md](reference.md).

## File set

| Piece | Path |
|-------|------|
| Parser / Sync / Categories | `Infrastructure/Trackers/{Name}/` |
| Cron | `Controllers/Cron/{Name}Controller.cs` → `/cron/{slug}/…` |
| Details (optional) | `Models/Details/{Name}Details.cs` |
| Icon | `web/public/img/ico/{slug}.ico` (that site’s favicon) |
| Fixtures / tests | `tests/JacRed.Tests/Fixtures/{Name}/`, `{Name}ParserFixtureTests.cs` |
| Dry-run | `scripts/dry_run_{slug}_parser.py` |

### Wiring checklist

- `AppOptions`, `ConfigSchema` (slugs + block names), `AppConfigurationProvider` log case
- `TrackerServiceCollectionExtensions` DI
- `FileDB.UrlParsing` when URL id is non-obvious
- `Data/example.yaml|conf`, `Data/init.yaml|conf` (`synctrackers` + block; **no secrets**)
- `Data/crontab` via `run-job.sh` (stagger minutes; raise `max_time` for long crawls)
- `web/public/openapi.yaml` `TrackerSlug` → `cd web && npm run gen:api`
- Docs: `docs/trackers-and-parsing.md`, `docs/configuration.md` (auth), `docs/api.md` (cron), README count

**Runtime config:** binary loads `./init.yaml` from **CWD**, not `Data/init.yaml` (template only).

## Magnet / auth rules (critical)

- **Anibelka:** anonymous only — never cookie/login (passkeys).
- **Rutracker:** FlareSolverr Warmup; Anistar does **not** use that path (static CF cookie).
- **Ultradox:** google/yandex-like Referer or 503; magnets on detail pages.
- **RuDub / Mazepa:** `MagnetNoTrackers` when auth announce has passkey.
- **Successor sites:** new slug (e.g. `rudub`); never retarget predecessor (`baibako`).

## Quality / Batch

- Gate only when requested (rudub 1080/2160, lostfilm 1080/2160, subsplease `res==1080`).
- Batch/season packs via catalog/show crawl, not only latest window; mark `[Batch]`.
- JSON APIs: keep max useful fields + rich checkpoint even when filtering resolution.

## Verify

```bash
python3 scripts/dry_run_{slug}_parser.py
dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~{Name}
```

Report: slug, auth, sync cluster, magnet policy, quality gate, cron endpoints, test counts.

## Anti-patterns

- Half-wired tracker (missing OpenAPI / synctrackers / crontab / docs)
- Copied sibling `.ico`
- HTML scrape when a stable JSON API exists
- Colliding FDB URLs on multi-episode pages
- Committing live cookies/passwords
- Assuming `Data/init.yaml` is what the running process loads

## Related

- Subagent: [`.cursor/agents/jacred-tracker-parser.md`](../../agents/jacred-tracker-parser.md)
- Catalog: [reference.md](reference.md)
- Rutracker CF: `Infrastructure/Trackers/Rutracker/README.md`
- Ops docs: `docs/trackers-and-parsing.md`, `docs/configuration.md`, `docs/api.md`
