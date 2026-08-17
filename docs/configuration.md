---
title: Конфигурация
description: Параметры init.yaml / init.conf — sync, logging, search, Torznab, прокси
tags:
  - ops
  - config
---

# Конфигурация

!!! info "Приоритет и hot-reload"

    Приоритет файлов: **`init.yaml`** > **`init.conf`**. Если существуют оба, используется `init.yaml`. Конфиг перечитывается автоматически каждые 10 секунд.

Примеры полного конфига: [`Data/example.yaml`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.yaml), [`Data/example.conf`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.conf). В рабочем конфиге указывайте только те параметры, которые нужно изменить.

## Основные параметры

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
| `listenip` | IP для прослушивания (`any` — все интерфейсы) | `any` |
| `listenport` | Порт HTTP | `9117` |
| `apikey` | Ключ для поиска, Torznab, `/stats/*` JSON и прочих путей вне [белого списка](security.md#apikey). Передаётся: `?apikey=...`, `X-Api-Key`, `Authorization: Bearer`. Пусто — проверка отключена | — |
| `devkey` | Ключ для `/dev/`, `/cron/`, `/jsondb/*`, `/api/v1.0/config/*` из интернета или через туннель. **LAN-клиент** или **`devkey`** (`X-Dev-Key`, `?devkey=`). Reverse proxy (loopback или Docker + XFF) **без** devkey **не открывает** admin/config | — |
| `mergeduplicates` | Объединять дубликаты в выдаче | `true` |
| `mergenumduplicates` | Объединять дубликаты по номеру (серии и т.п.) | `true` |
| `openstats` | Открыть доступ к `/stats/*` | `true` |
| `opensync` | Разрешить отдачу базы через `/sync/fdb/*` | `false` |
| `web` | Раздавать статику (веб-интерфейс) | `true` |
| `maxreadfile` | Макс. число открытых файлов за один поисковый запрос | `200` |
| `evercache` | Кеш открытых файлов (рекомендуется при высокой нагрузке) | см. ниже |
| `fdbPathLevels` | Уровни вложенности каталогов fdb (влияет на структуру хранения данных) | `2` |

### Настройки кеша (evercache)

Кеш открытых файлов БД для повышения производительности при высокой нагрузке:

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
| `enable` | Включить кеш | `false` |
| `validHour` | Время жизни кеша в часах | `1` |
| `maxOpenWriteTask` | Максимальное число открытых задач записи | `200` |
| `dropCacheTake` | Количество элементов для удаления из кеша при переполнении | `200` |

Пример конфигурации:

```yaml
evercache:
  enable: true
  validHour: 1
  maxOpenWriteTask: 200
  dropCacheTake: 200
```

## Синхронизация

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
| `syncapi` | URL upstream-сервера с `opensync: true` | `""` |
| `opensync` | Разрешить отдачу базы через `/sync/fdb/*` | `false` |
| `synctrackers` | Фильтр трекеров при pull (slug’и из `TrackerSlug` / `ConfigSchema.KnownTrackerSlugs`) | см. example |
| `disable_trackers` | Исключить трекеры из sync и API `GET /api/v1.0/trackers` | `[]` |
| `timeSync` | Интервал pull torrents, мин | `120` |
| `timeSyncSpidr` | Интервал Spidr pull, мин | `360` |
| `syncsport` | Синхронизировать sport | `false` |
| `syncspidr` | Spidr-режим (облегчённые записи) | `false` |

**Эндпоинты upstream/downstream:**

| Маршрут | Назначение |
| --- | --- |
| `GET /sync/conf` | `{ fbd, spidr, version: 2 }` |
| `GET /sync/fdb/torrents?time=&start=&spidr=` | Основной batch sync |

Клиент `SyncCron` требует `fbd: true` в `/sync/conf`.

## Логирование

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
| `logFdb` | Писать лог добавлений/обновлений в Data/log/fdb.*.log | `true` |
| `logFdbRetentionDays` | Хранить логи fdb не более N дней (0 — без ограничения) | `7` |
| `logFdbMaxSizeMb` | Макс. суммарный размер логов fdb, МБ (0 — без ограничения) | `0` |
| `logFdbMaxFiles` | Макс. число файлов логов fdb (0 — без ограничения) | `0` |
| `logParsers` | Включить логи парсеров по трекерам (Data/log/{tracker}.log) | `true` |

### Консольное логирование (`logging:`)

Опциональный блок в `init.yaml` — уровни для journalctl. **Файловые** логи (`logFdb`, `logParsers`, `trackslog`) настраиваются отдельно выше.

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
| `logging.defaultLevel` | Минимальный уровень консоли | `Information` |
| `logging.consoleTimestamp` | Время в строке сообщения (journald и так пишет время) | `false` |
| `logging.tracksConsoleDetail` | Подробный вывод tracks в консоль | `false` |
| `logging.cronSkipFastMs` | HTTP `/cron/` быстрее N ms со status 200 → Debug | `100` |
| `logging.categories` | Уровни по категориям: `tracks`, `sync`, `sync_spidr`, `cron`, `fdb`, `stats`, `parsers` | `parsers: None` |

Пример (production master):

```yaml
logging:
  tracksConsoleDetail: false
  cronSkipFastMs: 100
  categories:
    tracks: Warning
    fdb: Warning
    parsers: None
```

Поиск в journald:

```bash
journalctl -u jacred -g 'sync_spidr:'
journalctl -u jacred -g 'cron:' -p warning
journalctl -u jacred -g 'tracks:' -p warning
journalctl -u jacred -g 'fdb:' -p warning
```

Префиксы в консоли: `tracks:`, `sync:`, `sync_spidr:`, `cron:`, `fdb:`, `stats:`, `trackers:`, `config:`.

## Статистика и треки

Параметры `timeStatsUpdate`, модуль **tracks** (TorrServer / ffprobe), файлы статистики, typetask и матрица concurrency — в отдельном документе: [tracks.md](tracks.md).

Кратко: `tracks: false` по умолчанию; при включении нужны `tsuri` и (обычно) уникальный `trackscategory`.

## Трекеры (блоки в конфиге) {#trackers-config}

Для каждого трекера можно задать следующие параметры:

| Параметр | Описание | Пример |
| --- | --- | --- |
| `host` | Основной URL трекера | `https://rutracker.org` |
| `alias` | Альтернативный URL (например, .onion адрес) | `http://rutracker....onion` |
| `useproxy` | Использовать прокси для этого трекера | `true` / `false` |
| `reqMinute` | Максимальное число запросов в минуту | `8` |
| `parseDelay` | Задержка между запросами при парсинге, мс | `7000` |
| `log` | Включить логи парсера для этого трекера (Data/log/{tracker}.log) | `true` |
| `login` | Учётные данные (u — username, p — password), если трекер требует логин | `{u: "user", p: "pass"}` |
| `cookie` | Статическая cookie-сессия (часто альтернатива `login`) | `"session=value"` |

Полный список трекеров и значения по умолчанию — в [`Data/example.yaml`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.yaml) / [`Data/example.conf`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.conf).

**Аутентификация отдельных трекеров** (плейсхолдеры — в [`Data/example.yaml`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.yaml); реальные секреты не коммитьте):

| Трекер | Что нужно |
| --- | --- |
| **Korsars** | `login.u` / `login.p` **или** статическая cookie с `bb_data` (если задана cookie — логин не обязателен) |
| **Anifilm** | `login` **или** session cookie (например `XSRF-TOKEN` + session) |
| **Anistar** | Статическая cookie (`cf_clearance` + session) обязательна для live-парса; получить экспортом из браузера или через FlareSolverr вручную. **Не** использует блок `flaresolverr` / `/cron/cloudflare/Warmup` как Rutracker |
| **Anibelka** | Только анонимно — **не** задавайте `cookie` / `login` (в раздачах есть passkey) |
| **SubsPlease** | Только анонимно — JSON API; 1080p magnets only; Batches via `ParseShows` |
| **RuDub** | `login` **или** cookie (`PHPSESSID` / `uid` / `pass`); парсит только HD 1080 / HD 2160; зеркала `rN.rudub.world` через `host`/`alias` |
| **Ultradox** | Логин не нужен; Referer должен выглядеть как поиск google/yandex (свой origin → 503) |
| **Rutracker** | См. [`Infrastructure/Trackers/Rutracker/README.md`](https://github.com/jacred-fdb/jacred/blob/main/Infrastructure/Trackers/Rutracker/README.md) и [парсинг](trackers-and-parsing.md) |
| **Baibako / Lostfilm / Animelayer / …** | См. блоки в [`Data/example.yaml`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.yaml) |

## Прокси

Настройки прокси позволяют маршрутизировать запросы через прокси-серверы.

### Общие настройки прокси (`proxy`)

Используются для всех запросов, если не переопределены в `globalproxy`:

| Параметр | Описание | Пример |
| --- | --- | --- |
| `pattern` | Регулярное выражение для сопоставления URL | `"\\.onion"` |
| `list` | Список прокси-серверов | `["socks5://127.0.0.1:9050"]` |
| `useAuth` | Использовать аутентификацию | `true` / `false` |
| `username` | Имя пользователя для прокси | `"user"` |
| `password` | Пароль для прокси | `"pass"` |
| `BypassOnLocal` | Обходить прокси для локальных адресов | `true` / `false` |

### Глобальные правила прокси (`globalproxy`)

Массив правил для применения к определённым доменам/паттернам. Правила проверяются по порядку, используется первое совпадение.

Пример для доменов `.onion` через Tor:

```yaml
globalproxy:
  - pattern: "\\.onion"
    list:
      - socks5://127.0.0.1:9050
    useAuth: false
    BypassOnLocal: false
```

## Пример минимального конфига

=== "YAML (`init.yaml`)"

    ```yaml
    listenport: 9120
    syncapi: https://jacred.example.com

    search:
      mergeV1: auto
      skipCatFilter: true

    alloha:
      enable: true
      token: "YOUR_ALLOHA_TOKEN"

    torznab:
      enable: true

    NNMClub:
      alias: http://nnmclub....onion

    globalproxy:
      - pattern: "\\.onion"
        list:
          - socks5://127.0.0.1:9050
    ```

=== "JSON (`init.conf`)"

    ```json
    {
      "listenport": 9120,
      "syncapi": "https://jacred.example.com",
      "NNMClub": { "alias": "http://nnmclub....onion" },
      "globalproxy": [
        { "pattern": "\\.onion", "list": ["socks5://192.168.1.1:9050"] }
      ],
      "search": {
        "mergeV1": "auto",
        "maxV1Pairs": 4,
        "v1Sort": "sid",
        "stripTrailingYear": true,
        "stripSeasonEpisode": true,
        "skipSeasonEpisodeFilter": false,
        "skipCatFilter": true
      },
      "alloha": {
        "enable": true,
        "token": "YOUR_ALLOHA_TOKEN"
      },
      "torznab": {
        "enable": true,
        "enrichTitles": true
      }
    }
    ```

### Combined search (`search`)

Настройки поиска для **`/api/v2.0/indexers/.../results`** (Lampa, Jackett JSON) и Torznab XML (те же `SearchCombinedAsync`).

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
| `mergeV1` | Fuzzy v1-merge: `false` / `auto` / `true` | `auto` |
| `maxV1Pairs` | Лимит v1-запросов при `mergeV1=auto` (fuzzy) | `4` |
| `v1Sort` | Сортировка v1 (`sid` = seeders; также IMDB/KP) | `sid` |
| `stripTrailingYear` | Доп. вариант fuzzy-запроса без года | `true` |
| `stripSeasonEpisode` | Доп. вариант fuzzy-запроса без S01/S01E01 | `true` |
| `skipSeasonEpisodeFilter` | Не фильтровать по `season`/`ep` на сервере (AIOStreams) | `false` |
| `skipCatFilter` | Не фильтровать по `cat` / `Category[]` на сервере | `true` |

**`mergeV1: auto`** — v1 fuzzy **только в fuzzy mode** (Torznab text search, Lampa global search). Card mode (Lampa: `title` + `title_original`) — только v2 exact, без v1 fuzzy.

**AIOStreams / Sonarr-style `q=Show S01E01`:** при `stripSeasonEpisode: true` fuzzy ищет и полное `q`, и имя шоу без сезона/эпизода (`silo S01` → `silo`; `укрытие 2023 S01E01` → `укрытие 2023` → `укрытие` вместе с `stripTrailingYear`). `skipSeasonEpisodeFilter: true` отдаёт все релизы шоу — клиент фильтрует эпизоды сам.

| `mergeV1` | Card (Lampa карточка) | Fuzzy (Query / Torznab) |
| --- | --- | --- |
| `false` | v2 only | v2 only |
| `auto` | v2 only | v2 + v1 fuzzy (до `maxV1Pairs`) |
| `true` | v2 + v1 fuzzy (без лимита) | v2 + v1 fuzzy (без лимита) |

IMDB/KP/TMDB (`tt…`, `kp…`, `tmdb…`, themoviedb.org URL) всегда через v1 exact (после резолва Alloha), независимо от `mergeV1`.

Jackett JSON (`/api/v2.0/indexers/.../results`) **всегда** использует combined search; на `torznab.enable` не зависит.

### Alloha (`alloha`)

Резолв Kinopoisk / IMDb / TMDB ID → названия через **Alloha TV API v2** (`GET /v2/movies/search`), затем точный поиск в FileDB.

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
| `enable` | Включить резолв `tt…` / `kp…` / `tmdb…` / themoviedb.org URL | `true` |
| `baseUrl` | Хост API | `https://apbugall.org` |
| `token` | Bearer-токен (`Authorization`) | — (см. `example.yaml`) |
| `timeoutSeconds` | Таймаут HTTP | `8` |
| `cacheHours` | Memory-cache ID → titles | `24` |
| `filterByYear` | Если клиент не передал year — фильтр FileDB по году Alloha (±1) | `true` |

### Torznab XML (`torznab`)

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
| `enable` | Torznab XML и Prowlarr/Jackett Torznab-алиасы | `true` |
| `enrichTitles` | Озвучки в Torznab `<title>` | `true` |

При `enable: false` Torznab XML-эндпоинты и Prowlarr meta (`/api/v1/indexer`, `/api/v1/search`) отвечают **404**. Jackett JSON для Lampa продолжает работать.

| URL | Назначение |
| --- | --- |
| **`GET /torznab/api`** | Основной Torznab endpoint (`t=caps`, `search`, `tvsearch`, `moviesearch`, `indexers`) |
| **`GET /api/v2.0/indexers/{id}/results/torznab/api`** | Jackett-совместимый путь (алиас, тот же обработчик) |
| **`GET /api/v1/indexer/{id}/newznab`** | Prowlarr-совместимый путь (алиас, тот же обработчик) |
| **`GET /api/v1/search`** | Prowlarr Search Feed (JSON releases) |

#### Клиент → URL → формат

| Клиент | URL (относительно `http://host:9117`) | Формат |
| --- | --- | --- |
| **Lampa** | `/api/v2.0/indexers/all/results` | Jackett JSON (`Results[]`). Тип парсера в Lampa: **Jackett**, не Prowlarr/Torznab |
| **Sonarr / Radarr** | `/torznab/api` | Torznab XML (Generic Torznab indexer) |
| **AIOStreams** | `/torznab/api` | Torznab XML; `q=Show S01` stripping; `skipSeasonEpisodeFilter: true` если клиент фильтрует эпизоды |
| **Prowlarr** (ручная настройка Generic Torznab) | `/torznab/api` | Torznab XML |
| **qui / autobrr** (discover, backend=**jackett**) | `/api/v2.0/indexers/all/results/torznab/api` | Torznab XML + `t=indexers` discover |
| **qui / autobrr** (discover, backend=**prowlarr**) | `/api/v1/indexer` + `/api/v1/indexer/1/newznab` (+ `/api/v1/search`) | Prowlarr REST + Torznab XML / Search JSON |
| **JacRed native API** | `/api/v1.0/torrents` | Собственный JSON API (не Torznab, не Jackett) |

В ответе `t=caps` поле `<server url="...">` и `<atom:link rel="self">` в RSS указывают на **фактический путь запроса** (например Jackett- или Prowlarr-алиас), а не всегда на `/torznab/api`.

#### Sonarr / Radarr / Prowlarr (Generic Torznab)

```text
http://jacred:9117/torznab/api
```

API key — значение `apikey` из конфига (query `?apikey=...` или заголовок `X-Api-Key`).
