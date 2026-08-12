---
title: API
description: OpenAPI, поиск, Config API, /dev/*, cron, maintenance
tags:
  - api
  - reference
---
# API

## OpenAPI / Swagger

Спецификация: OpenAPI **3.0.3**, `info.version` **1.2.1** (источник: [`web/public/openapi.yaml`](https://github.com/jacred-fdb/jacred/blob/main/web/public/openapi.yaml)). В описании — список `TrackerSlug`, схема `BackgroundJob`, Torznab HEAD и общие query-параметры.

| URL | Назначение |
| --- | --- |
| `GET /swagger` | Swagger UI (интерактивная документация) |
| `GET /swagger/v1/swagger.json` | OpenAPI 3.0 JSON (конвертируется из `web/public/openapi.yaml` → publish `wwwroot/openapi.yaml`) |
| `GET /openapi.yaml` | Статическая OpenAPI 3.0 YAML (source: `web/public/openapi.yaml`) |

Swagger UI по умолчанию загружает **`/openapi.yaml`**; в выпадающем списке также доступен JSON (`/swagger/v1/swagger.json`).

При настроенном `apikey` пути `/swagger`, `/swagger/*` и `/openapi.yaml` доступны без ключа (как `/health`). Схемы авторизации в UI: `apikey` (query), `X-Api-Key`, `Authorization: Bearer`, `X-Dev-Key` (для Config API).

В спецификацию входят публичные эндпоинты (`/api/*`, `/torznab/*`, `/stats/*`, `/sync/*`, `/health`, `/health/background-jobs`, …). Пути `/cron/*`, `/dev/*`, `/jsondb/*` в OpenAPI **не описаны** (политика DevAdmin) — см. Controllers и [`Data/crontab`](https://github.com/jacred-fdb/jacred/blob/main/Data/crontab).

Типы для веб-UI: `cd web && npm run gen:api` → [`web/src/lib/api/types.ts`](https://github.com/jacred-fdb/jacred/blob/main/web/src/lib/api/types.ts).

Проверка соответствия маршрутов политикам: [`access-matrix.md`](access-matrix.md).

## Основные эндпоинты

- **`GET /`** — веб-интерфейс поиска (если `web: true`).
- **`GET /stats`** — страница статистики SPA (если `web: true`; данные — `/stats/torrents`, `/stats/meta`).
- **`GET /settings`** — настройки SPA (Config API: LAN или `X-Dev-Key`).
- **Веб-UI:** Vue 3 SPA в [`web/`](https://github.com/jacred-fdb/jacred/tree/main/web) (Vite + Tailwind + shadcn-vue); `make web` / [`./scripts/build-web-ui.sh`](https://github.com/jacred-fdb/jacred/blob/main/scripts/build-web-ui.sh) собирает publish-папку `wwwroot/` (в git не хранится).
- **`GET /health`** — проверка работы. Ответ JSON: `{"status":"OK"}`.
- **`GET /health/background-jobs`** — активные in-process ParseAll / UpdateTasks (cron). Ответ JSON: `{"jobs":[…]}` (пустой массив, если ничего не запущено). Page-only парсеры (`anistar`, `leproduction`, `viruseproject`, `anifilm`) сюда обычно **не** попадают.
- **`GET /version`** — версия приложения. Ответ JSON: `{"version":"1.0.0"}`.
- **`GET /lastupdatedb`** — дата/время последнего обновления БД (UTC). Ответ JSON: `{"lastupdatedb":"dd.MM.yyyy HH:mm"}`.

## API поиска

Сводная таблица «клиент → URL → формат» — в [Torznab XML](configuration.md#torznab-xml-torznab).

- **`GET /api/v2.0/indexers/{status}/results`** — поиск в формате Jackett JSON (**Lampa** и др.).
  - Combined search (`search.*`): v2 card/fuzzy + v1 fuzzy (только fuzzy mode при `mergeV1: auto`) + IMDB/KP/TMDB exact (Alloha v2 ID→title, alternative_name, type hint) + card fallback.
  - Параметры Lampa: `Query`, `title`, `title_original`, `year`, `is_serial`, `genres`, `Category[]`, `Tracker[]`, `season`, `ep`, `limit`, `offset`, `apikey`.
  - Ответ: `{ "Results": [...], "jacred": true }` с `ffprobe`, `languages`, `info` при `tracks: true`.
- **`GET /api/v2.0/indexers`** — список индексаторов (Jackett/Prowlarr).
- **`GET /api/v1/indexer`** — список индексаторов в формате Prowlarr REST API (qui/autobrr discover fallback).
- **`GET /api/v1/indexer/{id}`** — детали индексатора Prowlarr (`id=1`, для qui backend=prowlarr).
- **`GET /api/v1/indexer/{id}/newznab`** — Torznab XML через Prowlarr-совместимый путь (`t=caps|search|…`).
- **`GET /api/v1/search`** — Prowlarr Search Feed ([wiki](https://wiki.servarr.com/en/prowlarr/search#search-feed)): JSON-массив релизов.
  - Параметры: `query`, `type` (`search`|`tvsearch`|`movie`|`music`|`book`), `indexerIds` (`1`, `-2` torrents; `-1` usenet → пусто), `categories`, `limit`, `offset`, `apikey`.
  - Brace-токены в `query` (как в UI Prowlarr): `{ImdbId:tt…}`, `{TmdbId:1315772}`, `{Season:1}`, `{Episode:2}` и т.п. ID-запросы (`tt…`/`kp…`/`tmdb…`/themoviedb.org URL/`{ImdbId:…}`/`{TmdbId:…}`) → Alloha v2.
  - Lampa (`parser_torrent_type=prowlarr`): `query` + `type=tvsearch|search` + `categories` — запрос поднимается до card-поиска как у Jackett (`title`/`title_original`/`year`, `is_serial` 1=фильм / 2=сериал).
  - Один агрегированный indexer `id=1`; ответ в схеме ReleaseResource (`guid`, `title`, `size`, `seeders`, `magnetUrl`, `categories`, …).
  - JacRed-расширения как у Jackett: `ffprobe`, `languages`, `info` при `tracks: true` (иначе поля опускаются / null).
- **`GET /torznab/api`** — Torznab XML, основной endpoint (`t=search|tvsearch|moviesearch|caps|indexers`).
- **`GET /api/v2.0/indexers/{id}/results/torznab/api`** — Torznab XML (Jackett-алиас, тот же обработчик).

  Параметры и поведение одинаковы для обоих Torznab-путей:
  - Параметры: `q`, `imdbid`, `season`, `ep`, `year`, `cat`, `title`, `title_original`, `is_serial`, `limit`, `offset`, `apikey`.
  - IMDB/KP/TMDB ID (`tt…`, `kp…`, `tmdb…`, themoviedb.org `/movie|tv/{id}-…`) → Alloha v2 title resolve (name / original_name / alternative_name, type), затем v1 FileDB с `exact=true` (год ±1 при `alloha.filterByYear`).
  - Card mode (Lampa): `title` + `title_original` + `year` + `is_serial` + `genres`.
  - Объединение v1+v2, bilingual `Русский / English`, post-filter по сезону/эпизоду/году/категории.
- **`GET /api/v1.0/torrents`** — поиск торрентов (собственный JSON API JacRed, не Torznab и не Jackett).
  - Параметры: `search` / связанные фильтры, `tracker` (один slug или список через запятую — значения `TrackerSlug`), `sort`, `type`, …
  - IMDB/KP/TMDB ID (`tt…`, `kp…`, `tmdb…`, themoviedb.org URL) → Alloha v2 title resolve, затем exact FileDB (+ год ±1 / `type` из Alloha, если клиент не задал).
- **`GET /api/v1.0/trackers`** — список доступных имён трекеров (`TrackerSlug[]` в OpenAPI): из `synctrackers`, иначе known slugs; записи из `disable_trackers` исключаются. Пустой `synctrackers: []` возвращает `[]` (скан БД не выполняется).
- **`GET /api/v1.0/qualitys`** — список доступных качеств.

## Управление конфигурацией (Config API)

REST API и страница **`/settings`** для редактирования **`init.yaml`** / **`init.conf`**.

**Доступ:** политика **ConfigApi** — LAN-клиент **или** `devkey`. Reverse proxy (loopback или Docker + XFF) без devkey **недостаточен**. При заданном `apikey` — также ключ API для путей вне белого списка.

| Метод | Путь | Описание |
| --- | --- | --- |
| `GET` | `/api/v1.0/config` | Текущий конфиг (`data` + `content`, метаданные файла) |
| `GET` | `/api/v1.0/config/schema` | Схема полей для формы настроек |
| `POST` | `/api/v1.0/config/validate` | Валидация без записи на диск |
| `POST` | `/api/v1.0/config/diff` | Diff с текущим конфигом (перед сохранением) |
| `POST` | `/api/v1.0/config/render` | Объект формы → YAML/JSON текст |
| `POST` | `/api/v1.0/config/parse` | YAML/JSON текст → объект |
| `POST` | `/api/v1.0/config/format` | Нормализация и форматирование |
| `POST` | `/api/v1.0/config` | Сохранение (атомарная запись; hot-reload ~10 с) |

Тело запросов: `{ "data": { ... } }` (форма) и/или `{ "content": "...", "format": "yaml" }` (текстовый редактор). Подробности — в **`/openapi.yaml`**.

## Прочее управление

- **`GET /api/v1.0/conf`** — проверка apikey (`?apikey=...`).
- **`GET /jsondb/save`** — сохранить БД на диск (при использовании syncapi скрипт установки не вызывает save; при собственном парсинге cron вызывает save по расписанию).
  - Доступ: политика **DevAdmin** — LAN или `devkey`; при `apikey` — также ключ для middleware (см. [Безопасность](security.md)).

## Разработка и отладка {#dev-debug}

- **`GET /dev/*`** — инструменты разработки и отладки БД.
  - Доступ: политика **DevAdmin** — LAN или `devkey` (см. [Безопасность](security.md)).

| Эндпоинт | Описание |
| --- | --- |
| **`/dev/UpdateSize`** | Пересчитывает поле `size` (байты) из `sizeName` для всех торрентов. Обновляет `updateTime`. |
| **`/dev/ResetCheckTime`** | Сбрасывает `checkTime` на вчера для всех торрентов (для повторной проверки). |
| **`/dev/UpdateDetails`** | Обновляет детали торрентов через `updateFullDetails` (качество, сезоны и т.п.). |
| **`/dev/UpdateSearchName`** | Пересчитывает `_sn` и `_so` из `name`/`originalname`, мигрирует торренты при смене ключа бакета. |
| **`/dev/FixKnabenNames`** | Нормализует имена торрентов Knaben: убирает метаданные из title, оставляет базовое имя. Исправляет поиск в API v1/v2. Возвращает `{ ok, processed, updated, migrated }`. |
| **`/dev/FixBitruNames`** | Нормализует name/originalname торрентов Bitru: убирает сезон, эпизод, качество. Исправляет поиск в API v1/v2. Возвращает `{ ok, processed, updated, migrated }`. |
| **`/dev/FindCorrupt`** | Сканирует БД на повреждённые записи (null Value, пустые name/originalname/trackerName). Только чтение. Параметр: `?sampleSize=20`. |
| **`/dev/RemoveNullValues`** | Удаляет записи, где `torrent.Value == null` (битые ссылки). |
| **`/dev/FindDuplicateKeys`** | Ищет дубликаты ключей вида `X:X` (например `ponies:ponies`). Параметры: `?tracker=lostfilm`, `?excludeNumeric=false`. |
| **`/dev/RemoveBucket`** | Удаляет бакет по ключу. Параметры: `?key=ponies:ponies` — удалить; `?key=...&migrateName=...&migrateOriginalname=...` — перенести торренты в новый бакет. |
| **`/dev/FindEmptySearchFields`** | Ищет торренты с пустыми `_sn` или `_so`. Только чтение. Параметр: `?sampleSize=20`. |
| **`/dev/FixEmptySearchFields`** | Заполняет пустые `_sn`/`_so` из name/originalname/title, мигрирует при смене ключа. Пересобирает fastdb. |
| **`/dev/MigrateAnilibertyUrls`** | Мигрирует торренты Aniliberty на URL с хешем из magnet (`?hash=...`). |
| **`/dev/RemoveDuplicateAniliberty`** | Удаляет дубликаты Aniliberty по хешу magnet, оставляет запись с последним `updateTime`. |
| **`/dev/FixAnimelayerDuplicates`** | Устраняет дубликаты Animelayer: нормализует HTTP→HTTPS, удаляет HTTP-дубликаты. |
| **`/dev/FixKinozalDomainDuplicates`** | Схлопывает дубли Kinozal после смены домена (`.tv`→`.guru`): группирует по `details.php?id=`, оставляет канонический хост из `Kinozal.host`, переписывает одиночные старые URL. Возвращает `{ ok, scanned, rewritten, merged, removed, canonicalHost }`. |
| **`/dev/TracksStats`** | Статистика ffprobe/tracks (кэш `Data/temp/tracks-stats.json`, обновляется вместе с `stats.json` по `timeStatsUpdate`). Параметры: `?includeTorrentDb=true`, `?refresh=true` — принудительный пересчёт (игнорирует отложенный сбор при пустом index). |
| **`/dev/ExportTracks`** | Экспорт ffprobe в JSON для lampa-tracks/R2. Параметры: `?dir=Data/tracks-export`, `?dryRun=true`, `?includeTorrentDb=true`, `?background=true`. Формат: `{aa}/{b}/{hash}.json`, тело `{ "streams": [ ... ] }`. |
| **`/dev/ExportTracksStatus`** | Статус фонового экспорта (см. `ExportTracks` с `background=true`). |
| **`/dev/BackfillTracks`** | Миграция `Data/tracks`: файлы без расширения → `.json`, дописывание недостающих из FileDB. Параметры: `?dryRun=true`, `?migrateLegacy=true`, `?includeTorrentDb=true`. |

**Хранение tracks (`Data/tracks/`):**

- Канонический layout (JacRed + lampa-tracks): `{aa}/{b}/{hash}.json` — **lowercase hex** (совпадает с hash-значением).
- Чтение поддерживает uppercase export и файлы без `.json`.
- **`BackfillTracks`** приводит файлы к `.json` и нормализует регистр в canonical lowercase layout.
- При сохранении через модуль tracks устаревшие форматы файлов удаляются автоматически.
- Для массовой миграции — **`/dev/BackfillTracks`** (сначала `?dryRun=true`).

Примеры:

```bash
curl -s 'http://127.0.0.1:9117/dev/TracksStats'
curl -s 'http://127.0.0.1:9117/dev/TracksStats?refresh=true'
curl -s 'http://127.0.0.1:9117/dev/ExportTracks?dryRun=true'
curl -s 'http://127.0.0.1:9117/dev/ExportTracks?dir=Data/tracks-export'
curl -s 'http://127.0.0.1:9117/dev/BackfillTracks?dryRun=true'
curl -s 'http://127.0.0.1:9117/dev/ExportTracksStatus'
```

## Статистика и синхронизация

**Сводки (для UI `/stats` и API):**

| Эндпоинт | Ответ |
| --- | --- |
| `GET /stats/torrents` | Массив из `stats.json` |
| `GET /stats/tracks` | `{ ok, updatedAt, fromCache, stats }` из `tracks-stats.json` |
| `GET /stats/meta` | `{ ok, updatedAt, updatedAtLocal, tracksStatsUpdatedAt }` |

- Force refresh tracks: **`GET /dev/TracksStats?refresh=true`**
- **`GET /sync/*`** — эндпоинты синхронизации (если `opensync: true`).
  - **`GET /sync/fdb/torrents`** — основной протокол синхронизации (collections + pagination).

## Парсинг трекеров {#parsing-trackers}

Общие маршруты (не все трекеры реализуют каждый):

- **`GET /cron/{tracker}/parse`** — запуск парсинга (часто с `?page=` / `?limit_page=` / `?fullparse=` — зависит от трекера).
- **`GET /cron/{tracker}/ParseLatest`** — свежие раздачи (Rutor-style: anibelka, korsars, ultradox и ряд старых трекеров).
- **`GET /cron/{tracker}/ParseAllTask`** — фоновый полный обход задач (регистрируется в `/health/background-jobs`).
- **`GET /cron/{tracker}/UpdateTasksParse`** — обновление очереди задач (тоже background-jobs).
- **`GET /cron/{tracker}/parseMagnet`** — парсинг магнет-ссылок (для поддерживающих трекеров).
- Дополнительные параметры: `parseFrom`, `parseTo`, `parseFromDate`, `pages` (зависит от трекера).

Долгие HTTP-джобы для anibelka / korsars / ultradox **не** отменяют работу при обрыве curl (`RequestAborted` не пробрасывается) — дождитесь ответа или смотрите лог `Data/log/{tracker}.log`.

### Новые трекеры (ориентир из [`Data/crontab`](https://github.com/jacred-fdb/jacred/blob/main/Data/crontab))

| Трекер | Типичные действия | Расписание в примере crontab |
| --- | --- | --- |
| **anistar** | `parse?limit_page=3` (нужна cookie) | daily `40 6` |
| **leproduction** | `parse?limit_page=3` | daily `45 6` |
| **viruseproject** | `parse?limit_page=3` | daily `50 6` |
| **anifilm** | `parse` (login/cookie; max_time 1800s) | daily `55 6` |
| **anibelka** | `parse`, `UpdateTasksParse`, `ParseAllTask`, `ParseLatest` | hourly + daily tasks |
| **korsars** | то же + login/`bb_data` | hourly + daily tasks |
| **ultradox** | то же (Referer search-like) | hourly + daily tasks |
| **rudub** | `parse?limit_page=10` (login/cookie; HD 1080/2160 only; max_time 1800s). Initial fill: `?limit_page=50` or `parseFrom`/`parseTo` (cap 100) | hourly `:40` |

Полный канон расписания и `max_time` — только в **`Data/crontab`** (через `Data/run-job.sh`).

### Knaben

- **`GET /cron/knaben/parse`** — свежие раздачи (по умолчанию `from=0`, `size=300`, `pages=1`, `orderBy=date`, `orderDirection=desc`, все TV+Movies категории). Параметры: `from`, `size` (≤300), `pages` (≤10), `query`, `hours`, `orderBy` (`date`|`seeders`|`peers`), `orderDirection` (`desc`|`asc`), `categories` (через запятую). Окно Knaben API: `from + size ≤ 10000`.
- **`GET /cron/knaben/backfill`** — заполнение архива по листовым подкатегориям `2001000`–`2008000` и `3001000`–`3008000`: сначала `asc` (старые), при достижении 10 000 — встречный `desc` (новые). Состояние: **`Data/temp/knaben_backfill.json`**. Параметры: `pages` (≤10), `size` (≤300), `reset=true` — начать заново. Категории ≤10 000 — `complete` за один проход; ≤20 000 — за два; больше 20 000 — `partial` (середина недоступна из‑за лимита API).
- **`GET /cron/knaben/backfillStatus`** — краткий статус checkpoint без запуска.

Пример (как в [`Data/crontab`](https://github.com/jacred-fdb/jacred/blob/main/Data/crontab)):

```text
12,32,52 * * * *  /opt/jacred/Data/run-job.sh knaben-parse http://127.0.0.1:9117/cron/knaben/parse 900
42 * * * *  /opt/jacred/Data/run-job.sh knaben-backfill "http://127.0.0.1:9117/cron/knaben/backfill?pages=10" 900
```

Ручной старт архива с `asc`:

```text
curl -s "http://127.0.0.1:9117/cron/knaben/parse?from=0&size=300&pages=10&orderBy=date&orderDirection=asc&categories=2001000"
```

## Обслуживание FDB (`/cron/maintenance` и CLI `maintain`)

Единый проход по FileDB (ключи бакетов + shard-файлы) на битые/устаревшие/несогласованные данные.

**Online (HTTP):** фоновый job (как `ParseAllTask`): `Check` сразу возвращает `ok` / `work`, результат — в `Status` и `Data/temp/maintenance-last.json`. Лимит wall-clock онлайн-джоба — 6 часов.

| Эндпоинт | Описание |
| --- | --- |
| **`/cron/maintenance/Check`** | Старт проверки. Параметры: `?mode=report\|safe\|full` (по умолчанию `report`), `?sampleSize=20`, `?excludeNumericXx=true`. |
| **`/cron/maintenance/Status`** | Текущий прогресс и последний отчёт. |

**Offline CLI** (без Kestrel/workers, без лимита 6ч): из каталога установки (рядом с `Data/`):

```bash
cd /opt/jacred
# Перед safe/full остановите сервис JacRed вручную (один процесс на Data/).
./JacRed maintain --mode=report
./JacRed maintain --mode=safe
./JacRed maintain --mode=full --sample-size=50

# Фон (stdout → лог):
nohup ./JacRed maintain --mode=report > Data/temp/maintain.log 2>&1 &
tail -f Data/temp/maintain.log
# Итог также в Data/temp/maintenance-last.json
```

По умолчанию `--mode=report` (только чтение). Ctrl+C отменяет проход. Прогресс пишется в stdout.

**Режимы `mode`:**

| Mode | Поведение |
| --- | --- |
| `report` | Только чтение: null Value, пустые name/originalname/trackerName/`_sn`/`_so`, ключи `X:X`, несовпадение ключа бакета, dict-key ≠ `url`, отсутствующие/пустые shard, orphan-файлы под `Data/fdb`, пустые magnet/types. |
| `safe` | Отчёт + удаление null, заполнение `_sn`/`_so` (с миграцией бакета при необходимости), удаление пустых бакетов, пересборка fastdb. |
| `full` | `safe` + миграция неверных бакетов, синхронизация dict-key с `url`, удаление masterDb-ключей без файла на диске, удаление orphan shard-файлов, удаление записей с пустыми magnet **и** types. |

Трекер-специфичные миграции (Knaben, Bitru, Aniliberty, Animelayer) остаются на `/dev/*`.

Примеры HTTP:

```bash
curl -s 'http://127.0.0.1:9117/cron/maintenance/Check'
curl -s 'http://127.0.0.1:9117/cron/maintenance/Check?mode=safe'
curl -s 'http://127.0.0.1:9117/cron/maintenance/Check?mode=full&sampleSize=50'
curl -s 'http://127.0.0.1:9117/cron/maintenance/Status'
```

**Доступ (HTTP):** политика **DevAdmin** (`/cron/*`). Подробные таблицы LAN / tunnel / ключи — в разделе [Безопасность и доступ к API](security.md).

HTTP-вызовы `/cron/*` логируются с префиксом `cron:` (уровень зависит от `logging.cronSkipFastMs`).

**Пример `curl` при включённых `apikey` и `devkey`:**

```bash
curl -s -H "X-Api-Key: YOUR_API_KEY" -H "X-Dev-Key: YOUR_DEV_KEY" \
  "http://127.0.0.1:9117/cron/rutor/parse"
```
