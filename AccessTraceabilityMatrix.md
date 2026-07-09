# JacRed — матрица трассировки доступа

**Источник истины (код):** `Infrastructure/Security/JacRedEndpointRegistry.cs`  
**Проверка:** `JacRedAccessCatalog.VerifyRegistry()` — выполняется при старте (несовпадения пишутся в лог)  
**Последняя проверка:** 2026-07-09 — все маршруты каталога совпадают с реестром  
**README (оператор):** [Безопасность и доступ](README.md#безопасность-и-доступ-к-api) · [Логирование](README.md#консольное-логирование-logging)

---

## Определения политик

| Политика | Правило middleware | Ключи |
|----------|-------------------|-------|
| **Public** | Всегда разрешено | — |
| **ConfigApi** | LAN-клиент **или** валидный devkey (одного same-host proxy **недостаточно**) | `X-Dev-Key`, `?devkey=` |
| **DevAdmin** | LAN-клиент **или** валидный devkey (одного same-host proxy **недостаточно**) | `X-Dev-Key`, `?devkey=` |
| **ApiKeyWhenConfigured** | Если `apikey` в конфиге не задан — открыто; иначе нужен валидный ключ | `?apikey=`, `X-Api-Key`, `Bearer` |

**Коды отказа:** OPTIONS → 204; ключ задан, но не передан → 401; иначе → 403.

**Сетевой контекст:** Client IP — после `X-Forwarded-For`; Peer IP — прямое TCP-подключение (см. `ClientNetworkContext`).

---

## Префикс пути → политика (реестр)

| Префикс / шаблон пути | Политика | Примечания |
|----------------------|----------|------------|
| `/dev/` | DevAdmin | Обслуживание и диагностика |
| `/cron/` | DevAdmin | Запуск синхронизации трекеров |
| `/jsondb`, `/jsondb/` | DevAdmin | Администрирование FileDB |
| `/api/v1.0/config` | ConfigApi | API настроек (секреты в ответе) |
| `/`, `/stats`, `/settings` | Public | Только HTML-оболочки |
| `/health`, `/version`, `/lastupdatedb` | Public | Health-пробы |
| `/api/v1.0/conf` | Public | Проверка apikey (Jackett) |
| `/sync/` | Public | Middleware пропускает; `opensync` в SyncController |
| `/swagger`, `/openapi.yaml` | Public | Документация API |
| `/css/`, `/js/`, `/img/`, `/vendor/`, `/fonts/` | Public | Статика (при `web=true`) |
| `/opensearch.xml`, `/manifest.json`, `/sw.js` | Public | Метаданные PWA |
| *всё остальное* | ApiKeyWhenConfigured | Поиск, JSON stats, torznab, jackett |

---

## Трассировка эндпоинтов (контроллер → политика)

### Public

| Маршрут | Контроллер | Вторичная проверка |
|---------|------------|-------------------|
| `GET /` | HomeController | — |
| `GET /stats` | HomeController | HTML-оболочка (JSON на `/stats/*` не публичный) |
| `GET /settings` | HomeController | HTML-оболочка |
| `GET /opensearch.xml` | HomeController | — |
| `GET /health` | HealthController | — |
| `GET /version` | HealthController | — |
| `GET /lastupdatedb` | HealthController | — |
| `GET /api/v1.0/conf` | HealthController | Подсказка о валидности apikey |
| `GET /sync/conf` | SyncController | — |
| `GET /sync/fdb` | SyncController | `opensync` |
| `GET /sync/fdb/torrents` | SyncController | `opensync` |
| `GET /sync/tracks/stats` | SyncController | `opensync` |
| `GET /swagger`, `/openapi.yaml` | Startup / Swagger | — |

### ConfigApi

| Маршрут | Контроллер |
|---------|------------|
| `GET/POST /api/v1.0/config` | ConfigController |
| `GET /api/v1.0/config/schema` | ConfigController |
| `POST /api/v1.0/config/validate` | ConfigController |
| `POST /api/v1.0/config/diff` | ConfigController |
| `POST /api/v1.0/config/render` | ConfigController |
| `POST /api/v1.0/config/parse` | ConfigController |
| `POST /api/v1.0/config/format` | ConfigController |

### DevAdmin

| Шаблон маршрута | Контроллер |
|-----------------|------------|
| `/dev/*` | DevMaintenanceController, DevDiagnosticsController, DevMigrationController, DevTracksController |
| `/jsondb/*` | DbController |
| `/cron/{tracker}/*` | Controllers/Cron/* (17 трекеров) |

### ApiKeyWhenConfigured

| Маршрут | Контроллер | Вторичная проверка |
|---------|------------|-------------------|
| `GET /api/v1.0/torrents` | TorrentsController | — |
| `GET /api/v1.0/qualitys` | TorrentsController | — |
| `GET /api/v2.0/indexers/{status}/results` | JackettController | — |
| `GET /torznab/api` | TorznabController | — |
| `GET /api/v2.0/indexers/{indexer}/results/torznab/api` | TorznabController | — |
| `GET /api/v1/indexer/{indexer}/newznab` | TorznabController | — |
| `GET /api/v2.0/indexers` | TorznabController | — |
| `GET /api/v1/indexer` | TorznabController | — |
| `GET /api/v1/indexer/{id}` | TorznabController | — |
| `GET /stats/trackers` | StatsController | `openstats`; summary stats.json |
| `GET /stats/trackers/new` | StatsController | `openstats`; create shard today |
| `GET /stats/trackers/updated` | StatsController | `openstats`; update shard today |
| `GET /stats/trackers/{name}` | StatsController | `openstats`; one tracker summary |
| `GET /stats/meta` | StatsController | `openstats` |
| `GET /stats/tracks` | StatsController | `openstats` |
| `GET /stats/torrents` | StatsController | `openstats` |
| `GET /stats/trackers/{name}/new` | StatsController | `openstats` |
| `GET /stats/trackers/{name}/updated` | StatsController | `openstats` |

---

## Доступ по контексту клиента

| Политика | Loopback / LAN | Same-host proxy (без devkey) | Удалённый / туннель |
|----------|----------------|------------------------------|---------------------|
| Public | ✓ | ✓ | ✓ |
| ConfigApi | ✓ | ✗ | нужен devkey |
| DevAdmin | ✓ | ✗ | нужен devkey (если задан в конфиге) |
| ApiKeyWhenConfigured | apikey, если задан | apikey, если задан | apikey, если задан |

---

## Результат проверки реестра

Все маршруты из `JacRedAccessCatalog.Routes` сверены с `JacRedEndpointRegistry.ResolvePolicy()` — **0 расхождений** (проверено при сборке/старте).

Повторная проверка после изменений:

```csharp
var errors = JacRedAccessCatalog.VerifyRegistry();
```

---

## Операционное логирование (journalctl)

Консольный вывод использует префиксы категорий для grep (`tracks:`, `sync:`, `sync_spidr:`, `cron:`, `fdb:`, …). **Файловые логи** в `Data/log/` **включены по умолчанию** (`logFdb`, `logParsers`, `trackslog`). Настройка консоли в `init.yaml`:

```yaml
logging:
  defaultLevel: Information
  consoleTimestamp: false
  tracksConsoleDetail: false   # компактный вывод tracks (только ошибки)
  cronSkipFastMs: 100          # HTTP 200 /cron/ быстрее 100 мс → Debug
  categories:
    tracks: Warning
    fdb: Warning
    parsers: None              # только в файл (ParserLog)
```

```bash
journalctl -u jacred -g 'sync_spidr:'
journalctl -u jacred -g 'cron:' -p warning
journalctl -u jacred -g 'tracks:' -p warning
journalctl -u jacred -g 'fdb:' -p warning
```
