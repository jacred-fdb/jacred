---
title: Архитектура
description: Слои JacRed ASP.NET Core и фоновые процессы
tags:
  - develop
  - architecture
---

# Архитектура

JacRed — **ASP.NET Core 10** (single project `JacRed.csproj`).

```mermaid
flowchart TB
  Controllers[Controllers HTTP]
  Application[Application поиск индекс]
  Infrastructure[Infrastructure FileDB трекеры security]
  Configuration[Configuration hot-reload]
  Models[Models DTO]
  Controllers --> Application
  Controllers --> Infrastructure
  Application --> Infrastructure
  Infrastructure --> Configuration
  Controllers --> Models
  Application --> Models
```

## Слои проекта

| Компонент | Путь | Назначение |
| --- | --- | --- |
| **Security** | `Infrastructure/Security/` | `JacRedEndpointRegistry`, `JacRedAuthorizationMiddleware`, `UseJacRedSecurity()` |
| **Logging** | `Infrastructure/Logging/` | `JacRedLog`, console categories, M.E.Logging |
| **FileDB** | `Infrastructure/Persistence/FileDB/` | Файловая БД, `masterDb`, cron fdb |
| **Search** | `Infrastructure/Indexers/`, `Application/Search/` | Jackett / Torznab / v1 torrents |
| **Trackers** | `Infrastructure/Trackers/{Name}/` | Parser + SyncService на трекер |
| **Background** | `Infrastructure/Background/` | `SyncWorker`, `StatsWorker`, `TrackersWorker`, `FileDbWorker`, `TracksWorker`, `FastDbRefreshWorker` |
| **Config** | `Configuration/AppConfigurationProvider.cs` | Загрузка, hot-reload, redaction |

## Фоновые процессы

```mermaid
flowchart LR
  SyncCron[SyncCron syncapi]
  TrackersCron[TrackersCron HTTP cron]
  StatsCron[StatsCron stats.json]
  TracksCron[TracksCron tsuri]
  FileDB[FileDB evercache]
  SyncCron --> FileDB
  TrackersCron --> FileDB
  StatsCron --> FileDB
  TracksCron --> FileDB
```

- **SyncCron** — pull с `syncapi` (`/sync/fdb/torrents`)
- **TrackersCron** — парсинг по HTTP `/cron/*` (внешний cron) + внутренние циклы
- **StatsCron** — `stats.json`, `tracks-stats.json`
- **TracksCron** — ffprobe через `tsuri` (если `tracks: true`)
- **FileDB cron** — evercache, ffprobe refresh
