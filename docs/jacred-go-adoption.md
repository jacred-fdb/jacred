# Порт трекеров из jacred-go → JacRed

Источник: [`temp/jacred-go`](../temp/jacred-go). Bitru HTML **исключён** (API уже есть).

## Gap summary

| Добавляем | Сложность | Cron |
|-----------|-----------|------|
| Anistar | S | `parse?limit_page=` |
| Leproduction | M | `parse?limit_page=` |
| Viruseproject | M | `parse?limit_page=` |
| Anifilm | M | `parse?fullparse=` |
| Anibelka | M | tasks (anonymous torrent→magnet) |
| Korsars | L | tasks trio + login |
| Ultradox | L | tasks + Referer + detail magnets |
| Mazepa MagnetNoTrackers | S | infra / security |

Вне scope: Bitru HTML, CloakBrowser/TLS replay, native ffprobe.

## Roadmap

1. **Wave 0** — `BencodeTo.MagnetNoTrackers` + Mazepa sync
2. **Wave 1** — Anistar → Leproduction → Viruseproject
3. **Wave 2** — Anifilm → Anibelka
4. **Wave 3** — Korsars → Ultradox

Wave 4 (CF registry / fetchmode / useragent) — **откатан**, не нужен при внешнем FlareSolverr.

## Wiring checklist (каждый трекер)

- [x] `Infrastructure/Trackers/{Name}/{Name}Parser.cs` + `{Name}SyncService.cs`
- [x] `Controllers/Cron/{Name}Controller.cs`
- [x] DI в `TrackerServiceCollectionExtensions.cs`
- [x] Slug в `ConfigSchema.KnownTrackerSlugs` + `TrackerBlockNames`
- [x] `AppOptions.{Name}` + `AppConfigurationProvider` log case
- [x] Блок в `Data/example.yaml` (+ crontab)
- [x] `scripts/dry_run_{slug}_parser.py --refresh-fixtures`
- [x] `tests/JacRed.Tests/Fixtures/{Name}/` + `{Name}ParserFixtureTests.cs`

## Infra done

- [x] Wave 0: `BencodeTo.MagnetNoTrackers` + Mazepa sync
- [x] Wave 4: **откатан** (CF registry / fetchmode / useragent)

## Outside scope (unchanged)

- Bitru HTML
- CloakBrowser / TLS replay
- Native ffprobe

---

```bash
python3 scripts/dry_run_{slug}_parser.py                  # score saved fixtures
python3 scripts/dry_run_{slug}_parser.py --refresh-fixtures
dotnet test tests/JacRed.Tests/JacRed.Tests.csproj --filter FullyQualifiedName~{Name}
```

Seed from Go when present: `temp/jacred-go/cron/anibelka/testdata/`, `temp/jacred-go/cron/ultradox/testdata/`.

---

## Agent prompts (copy-paste)

### Wave 0 — Mazepa MagnetNoTrackers

```
Port Go `TorrentBytesToMagnetNoTrackersErr` (temp/jacred-go/core/torrent_bencode.go) into
Infrastructure/Parsing/BencodeTo.cs as MagnetNoTrackers(byte[]).
Use it in MazepaSyncService after dl.php download (replace BencodeTo.Magnet).
Add unit test: magnet has xt+dn, no tr=/passkey announce.
Do not change listing NormalizeMagnet path unless needed.
```

### Wave 1a — Anistar

```
Port temp/jacred-go/cron/anistar/anistar.go into C# JacRed following Anidub/Selezen patterns.
Host default https://anistar.org. Endpoint GET /cron/anistar/parse?limit_page=.
Add dry_run_anistar_parser.py, fixtures, ParserFixtureTests.
Register slug anistar everywhere (schema, AppOptions, DI, example.yaml).
Acceptance: fixture tests pass; dry_run scores listing HTML.
```

### Wave 1b — Leproduction

```
Port temp/jacred-go/cron/leproduction/leproduction.go.
Host https://www.le-production.tv. Parse?limit_page=.
dry_run + fixtures + tests + full wiring. Slug: leproduction.
```

### Wave 1c — Viruseproject

```
Port temp/jacred-go/cron/viruseproject/viruseproject.go.
Host https://viruseproject.tv. One record per quality attachment.
dry_run + fixtures + tests. Slug: viruseproject.
```

### Wave 2a — Anifilm

```
Port temp/jacred-go/cron/anifilm/anifilm.go.
Host https://anifilm.pro. Support fullparse flag. CF/login optional via TrackerSettings.
dry_run (cookie env if needed) + fixtures + tests. Slug: anifilm.
```

### Wave 2b — Anibelka

```
Port temp/jacred-go/cron/anibelka/anibelka.go.
MUST stay anonymous (no login — passkey risk). Seed fixtures from Go testdata first.
Tasks: UpdateTasksParse / ParseAllTask / ParseLatest. Magnet from .torrent.
dry_run + tests. Slug: anibelka.
```

### Wave 3a — Korsars

```
Port temp/jacred-go/cron/korsars/korsars.go (phpBB-mod, login required, inline magnets).
Tasks trio. dry_run with --user/--password. Fixtures + tests. Slug: korsars. Support alias.
```

### Wave 3b — Ultradox

```
Port temp/jacred-go/cron/ultradox/ultradox.go.
Referer must look like google/yandex (not CF). Listing→detail; one record per quality magnet.
Seed Go testdata then dry_run. Tasks trio. Slug: ultradox. Host https://ultradox.onl.
```

### Wave 4 — откатан

CF registry / `fetchmode` / `useragent` не нужны при внешнем FlareSolverr + in-memory `MarkGuarded`.
