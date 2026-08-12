# JacRed tracker catalog (reference)

Read this when choosing a clone target or auth/magnet policy. Keep SKILL.md for workflow.

## Sync clusters

| Pattern | Slugs | Notes |
|---------|-------|-------|
| Rutor trio (+ ParseLatest) | rutor, kinozal, nnmclub, megapeer, toloka, torrentby, rutracker, anibelka, korsars, ultradox | Tasks → `Data/temp/{slug}_taskParse.json` |
| Page-range | baibako (0-based), anidub, animelayer, aniliberty, selezen, rudub (+ `limit_page`) | rudub cap ~100 pages |
| `limit_page` cats | anistar, leproduction, viruseproject; anifilm (`fullparse`) | Page-only; rarely in background-jobs |
| API latest + backfill | knaben, bitru, subsplease | Checkpoints under `Data/temp/` |
| Special | mazepa (all forums), lostfilm (`/new/` + season packs) | |

## Auth

| Auth | Slugs | Gotcha |
|------|-------|--------|
| CF + FlareSolverr | rutracker | Warmup ~5m before parse; not for Anistar |
| Static CF/session cookie | anistar | Manual/`cf_clearance` export |
| Login and/or cookie | kinozal, baibako, rudub, animelayer, anifilm, korsars (`bb_data`), selezen, toloka, mazepa, lostfilm | Prefer config cookie when set |
| Anon only | anibelka | Never login — passkeys in torrents |
| Anon + Referer trick | ultradox | google/yandex Referer; own origin → 503 |
| Public anon API/HTML | knaben, bitru, aniliberty, subsplease, leproduction, viruseproject, rutor, nnmclub, torrentby, anidub | |

**cp1251:** kinozal, nnmclub, megapeer, baibako, rudub, anistar.

## Magnet policy

| Policy | Slugs |
|--------|-------|
| `MagnetNoTrackers` | rudub, mazepa |
| t→M anon (full Magnet OK) | anibelka |
| t→M after login | baibako, animelayer, anistar, anifilm, toloka, lostfilm, megapeer, viruseproject, bitru, knaben fallback |
| Magnet HTML/API | rutor, nnmclub, torrentby, rutracker, korsars, selezen, anidub, aniliberty, leproduction, ultradox (detail), kinozal (hash→magnet), subsplease (`xl=`) |

## Per-slug one-liners

- **rutracker** — CF Flare · trio · magnet · warmup + `alias`
- **rutor** — anon · trio · magnet list · classic template
- **kinozal** — login/cookie · trio · hash→magnet · cp1251, domain churn
- **nnmclub** — anon (+onion alias) · trio · magnet · cp1251
- **megapeer** — anon · trio · t→M · cp1251 + cat Referer
- **bitru** — anon API · parse+backfill · t→M · cursor files
- **toloka** — login · trio · t→M
- **mazepa** — login · full crawl · NoTrk
- **torrentby** — anon · trio · magnet
- **selezen** — login/cookie · page-range · magnet · WAF-minimal headers
- **lostfilm** — cookie · /new/ + packs · t→M · 1080/2160, `#quality`
- **baibako** — login/cookie · page-range 0-based · t→M · do not retarget for RuDub
- **rudub** — login/cookie · `limit_page` · NoTrk · 1080/2160, cp1251, mirrors
- **animelayer** — login/cookie · page-range · t→M · re-login on empty
- **anidub** — anon · page-range 1-based · magnet prefer
- **anistar** — static CF cookie · `limit_page` cats · t→M · no JacRed Flare
- **anibelka** — anon only · trio · t→M
- **aniliberty** — anon · JSON torrents API · magnet
- **anifilm** — login/CSRF or cookie · cats + `fullparse` · t→M · prefer 1080
- **leproduction** — anon · `limit_page` cats · magnet
- **viruseproject** — anon · `limit_page` cats · t→M
- **korsars** — login or `bb_data` · trio · magnet · `rqHost`/`alias`
- **ultradox** — Referer trick · trio · magnet on detail
- **knaben** — anon API · parse+backfill+status · `knaben_backfill.json`, window ≤10000
- **subsplease** — anon API · parse+ParseShows(limit=50)+status · 1080 + Batch via `f=show`; `subsplease_shows.json`

## Dry-run scripts

Present: `anibelka`, `anifilm`, `anistar`, `bitru_api`, `kinozal`, `korsars`, `leproduction`, `lostfilm`, `nnmclub`, `rudub`, `rutor`, `rutracker`, `subsplease`, `torrentby`, `ultradox`, `viruseproject`.

Often missing: anidub, aniliberty, animelayer, baibako, knaben, mazepa, megapeer, selezen, toloka.

## Crontab

- Source of truth: `Data/crontab` + `Data/run-job.sh`
- Avoid stacking on rutor `1,16,31,46` and ultradox `13,28,43,58`
- CF warmup before rutracker; long crawls → `max_time` 900–1800s

## Docs touch list

`docs/trackers-and-parsing.md`, `docs/configuration.md` (auth), `docs/api.md` (cron + OpenAPI version), `docs/troubleshooting.md`, `docs/docker.md`, README tracker count.
