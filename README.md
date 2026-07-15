# JacRed

![Jacred — A Torrent aggregator & file database](wwwroot/img/jacred-social-preview.png)

[![Build](https://github.com/jacred-fdb/jacred/actions/workflows/build.yml/badge.svg)](https://github.com/jacred-fdb/jacred/actions/workflows/build.yml)
[![Release](https://github.com/jacred-fdb/jacred/actions/workflows/release.yml/badge.svg)](https://github.com/jacred-fdb/jacred/actions/workflows/release.yml)
[![GitHub release (latest SemVer)](https://img.shields.io/github/v/release/jacred-fdb/jacred?label=version)](https://github.com/jacred-fdb/jacred/releases)
[![GitHub tag (latest SemVer pre-release)](https://img.shields.io/github/v/tag/jacred-fdb/jacred?include_prereleases&label=pre-release)](https://github.com/jacred-fdb/jacred/tags)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Агрегатор торрент-трекеров с API в формате Jackett. Хранит данные в файловой БД (fdb), поддерживает синхронизацию с удалённой базой и самостоятельный парсинг трекеров по cron.

### Основные возможности

- 🔍 **Агрегация торрентов** с множества трекеров в единый API
- 📦 **Файловая БД (fdb)** для быстрого доступа к данным
- 🔄 **Синхронизация** с удалёнными серверами или самостоятельный парсинг
- 🎯 **API Jackett** — полная совместимость с форматом Jackett
- 📡 **Torznab XML** — встроенный Torznab API для Sonarr/Radarr/Prowlarr
- 🌐 **Веб-интерфейс** — поиск, статистика и редактор конфигурации
- ⚙️ **Настройки в браузере** — `/settings` (форма, YAML/JSON, валидация, diff перед сохранением)
- 📖 **OpenAPI / Swagger** — `/openapi.yaml`, интерактивная документация на `/swagger`
- 🔐 **Поддержка прокси** и Tor для доступа к .onion доменам
- 📊 **Статистика** по трекерам и торрентам
- 🎵 **Модуль tracks** для сбора метаданных треков (опционально)
- ⚡ **Кеширование** для высокой производительности
- 🐳 **Docker** поддержка для простого развёртывания

## AI Документация

[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/jacred-fdb/jacred)

---

## 📥 Поддержать проект

💲 **YooMoney (RUB):** [https://yoomoney.ru/fundraise/1FRDH2NBCE3.260210](https://yoomoney.ru/fundraise/1FRDH2NBCE3.260210)

💰 **TON / USDT:** `UQAFGIN19ZDeUQFC4SpHMg2dhjliSXq_vzUWYZMDJ8w_zSqo`

💴 **MIR (RUB):** `2204120115029460`

💸 **YooMoney (прямой перевод):** [https://yoomoney.ru/to/410015186713710](https://yoomoney.ru/to/410015186713710)

---

## Требования

- **.NET 10.0** (для запуска из исходников)
- Для установки скриптом: **Linux** (systemd, cron), рекомендуется Debian/Ubuntu
- **libicu** — на Linux (.NET использует ICU для глобализации). При запуске бинарника напрямую (без Docker) установите пакет:
  - **Debian/Ubuntu:** `apt install libicu-dev` или `libicu76` / `libicu72` (имя пакета зависит от версии дистрибутива)
  - **Alpine:** `apk add icu-libs` (в Docker-образе уже включено)

---

## Установка

Установка одной командой (запускать от любого пользователя, при необходимости запросится sudo):

```bash
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | bash
```

Скрипт устанавливает приложение в **`/opt/jacred`**, создаёт пользователя и systemd-сервис `jacred`, добавляет cron для сохранения БД и при первом запуске по желанию скачивает готовую базу.

**Опции:**

| Опция | Описание |
| ------- | ---------- |
| `--no-download-db` | Не скачивать и не распаковывать базу (только при установке) |
| `--pre-release` | Установить или обновить из последнего pre-release (например, 2.0.0-dev1) |
| `--update` | Обновить приложение с последнего релиза (сохранить БД, заменить файлы, перезапустить) |
| `--remove` | Полностью удалить JacRed (сервис, cron, каталог приложения) |
| `-h`, `--help` | Показать справку |

**Примеры:**

```bash
# Обычная установка (одна команда)
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | sudo bash

# Установка без загрузки базы (одна команда)
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | sudo bash -s -- --no-download-db

# Скачать скрипт и запустить с аргументами
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh -o jacred.sh
chmod +x jacred.sh
sudo ./jacred.sh --no-download-db

# Установка pre-release версии
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | bash -s -- --pre-release

# Или скачать и запустить pre-release
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh -o jacred.sh
chmod +x jacred.sh
sudo ./jacred.sh --pre-release

# Обновление уже установленного приложения
sudo /opt/jacred/jacred.sh --update

# Обновление до pre-release версии
sudo /opt/jacred/jacred.sh --update --pre-release

# Удаление
sudo /opt/jacred/jacred.sh --remove
```

Установка/обновление/удаление под конкретным пользователем (cron будет добавлен или удалён для этого пользователя):

```bash
sudo -u myservice ./jacred.sh
sudo -u myservice ./jacred.sh --update
sudo -u myservice ./jacred.sh --remove
```

После установки:

- Настройте конфиг: **`/opt/jacred/init.yaml`** или **`/opt/jacred/init.conf`**, либо через веб-редактор **`/settings`** (LAN или `devkey` — см. [Безопасность](#безопасность-и-доступ-к-api))
- Веб-интерфейс: **`http://127.0.0.1:9117/`** (поиск), **`/stats`**, **`/settings`**
- Перезапуск: `systemctl restart jacred`
- Полный crontab для парсинга: `crontab /opt/jacred/Data/crontab`

> **Важно:** по умолчанию синхронизация отключена: скрипт установки скачивает базу, парсинг — по cron (`Data/crontab`). Чтобы подтягивать базу с внешнего сервера, укажите `syncapi` и включите нужные опции синхронизации в конфиге.

---

## Конфигурация

Приоритет файлов: **`init.yaml`** > **`init.conf`**. Если существуют оба, используется `init.yaml`. Конфиг перечитывается автоматически каждые 10 секунд.

Примеры полного конфига: **`Data/example.yaml`**, **`Data/example.conf`**. В рабочем конфиге указывайте только те параметры, которые нужно изменить.

### Основные параметры

| Параметр | Описание | По умолчанию |
| ---------- | ---------- | -------------- |
| `listenip` | IP для прослушивания (`any` — все интерфейсы) | `any` |
| `listenport` | Порт HTTP | `9117` |
| `apikey` | Ключ для поиска, Torznab, `/stats/*` JSON и прочих путей вне [белого списка](#безопасность-и-доступ-к-api). Передаётся: `?apikey=...`, `X-Api-Key`, `Authorization: Bearer`. Пусто — проверка отключена | — |
| `devkey` | Ключ для `/dev/`, `/cron/`, `/jsondb/*`, `/api/v1.0/config/*` из интернета или через туннель. **LAN-клиент** или **`devkey`** (`X-Dev-Key`, `?devkey=`). Same-host proxy **без** devkey **не открывает** admin/config | — |
| `mergeduplicates` | Объединять дубликаты в выдаче | `true` |
| `mergenumduplicates` | Объединять дубликаты по номеру (серии и т.п.) | `true` |
| `openstats` | Открыть доступ к `/stats/*` | `true` |
| `opensync` | Разрешить отдачу базы через `/sync/fdb/*` | `false` |
| `web` | Раздавать статику (веб-интерфейс) | `true` |
| `maxreadfile` | Макс. число открытых файлов за один поисковый запрос | `200` |
| `evercache` | Кеш открытых файлов (рекомендуется при высокой нагрузке) | см. ниже |
| `fdbPathLevels` | Уровни вложенности каталогов fdb (влияет на структуру хранения данных) | `2` |

#### Настройки кеша (evercache)

Кеш открытых файлов БД для повышения производительности при высокой нагрузке:

| Параметр | Описание | По умолчанию |
| ---------- | ---------- | -------------- |
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

### Синхронизация

| Параметр | Описание | По умолчанию |
| ---------- | ---------- | -------------- |
| `syncapi` | URL upstream-сервера с `opensync: true` | `""` |
| `opensync` | Разрешить отдачу базы через `/sync/fdb/*` | `false` |
| `synctrackers` | Фильтр трекеров при pull | см. example |
| `disable_trackers` | Исключить трекеры из sync | `[]` |
| `timeSync` | Интервал pull torrents, мин | `120` |
| `timeSyncSpidr` | Интервал Spidr pull, мин | `360` |
| `syncsport` | Синхронизировать sport | `false` |
| `syncspidr` | Spidr-режим (облегчённые записи) | `false` |

**Эндпоинты upstream/downstream:**

| Маршрут | Назначение |
| ------- | ---------- |
| `GET /sync/conf` | `{ fbd, spidr, version: 2 }` |
| `GET /sync/fdb/torrents?time=&start=&spidr=` | Основной batch sync |

Клиент `SyncCron` требует `fbd: true` в `/sync/conf`.

### Логирование

| Параметр | Описание | По умолчанию |
| ---------- | ---------- | -------------- |
| `logFdb` | Писать лог добавлений/обновлений в Data/log/fdb.*.log | `true` |
| `logFdbRetentionDays` | Хранить логи fdb не более N дней (0 — без ограничения) | `7` |
| `logFdbMaxSizeMb` | Макс. суммарный размер логов fdb, МБ (0 — без ограничения) | `0` |
| `logFdbMaxFiles` | Макс. число файлов логов fdb (0 — без ограничения) | `0` |
| `logParsers` | Включить логи парсеров по трекерам (Data/log/{tracker}.log) | `true` |

#### Консольное логирование (`logging:`)

Опциональный блок в `init.yaml` — уровни для journalctl. **Файловые** логи (`logFdb`, `logParsers`, `trackslog`) настраиваются отдельно выше.

| Параметр | Описание | По умолчанию |
| ---------- | ---------- | -------------- |
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

### Статистика и треки

| Параметр | Описание | По умолчанию |
| ---------- | ---------- | -------------- |
| `timeStatsUpdate` | Интервал полного пересчёта статистики (`stats.json` + `tracks-stats.json`), мин. `-1` — отключить cron | `90` |
| `tracks` | Включить сбор метаданных треков (tsuri) | `false` |
| `trackslog` | Включить логи модуля tracks (Data/log/tracks.log) | `true` |
| `trackscategory` | Категория для торрентов из jacred (рекомендуется задавать уникально для каждого инстанса) | `jacred` |
| `tracksatempt` | Количество неудачных попыток извлечь дорожки, после этого торрент исключается из tracks | `20` |
| `tracksmod` | Режим треков: 0 — все, 1 — только за текущие сутки | `0` |
| `tracksdelay` | Задержка между запросами к tsuri, мс | `20000` |
| `tracksinterval` | Интервалы запуска задач tracks (task1 — за последние сутки, task0 — остальные), мин | `task1: 60, task0: 180` |
| `tsuri` | URL сервиса анализа треков (массив) | `["http://127.0.0.1:8090"]` |

**Файлы статистики** (каталог `Data/temp/`, один проход FDB по `timeStatsUpdate`):

| Файл | Назначение |
| ------ | ------------ |
| `stats.json` | Сводка по трекерам для UI `/stats` |
| `stats-meta.json` | `{ updatedAt, trackerCount }` — время последнего сбора |
| `tracks-stats.json` | Кэш export-статистики ffprobe/tracks (`/stats/tracks`, `/dev/TracksStats`) |
| `tracks-index.bz` | Gzip-индекс infohash в `Data/tracks` (быстрый старт и stats без walk всех JSON) |

**Эндпоинты (UI `/stats`):** `GET /stats/torrents` — сводка из `stats.json`; `GET /stats/tracks` — агрегат из `tracks-stats.json`; `GET /stats/meta` — `updatedAt`. Force refresh: `/dev/TracksStats?refresh=true`.

**Старт сервиса:** HTTP (`/health`) доступен через ~10–30 с после загрузки `masterDb.bz`. Индекс треков `Data/temp/tracks-index.bz` и первый сбор stats выполняются **в фоне**; пока индекс пуст, cron stats **откладывается** (в логе: `stats: deferred`). После rebuild индекса stats запускается автоматически.

**Счётчики tracks (confirm/wait/skip)** в `stats.json`: `confirm` — ffprobe в записи торрента, трек в RAM/индексе или файл с непустым `streams` (без полной загрузки JSON, как `TracksDB.Get`); `wait` — magnet есть, трека нет; `skip` — `ffprobe_tryingdata ≥ 3`.

Результаты анализа сохраняются в **`Data/tracks/{aa}/{b}/{hash}.json`**. Экспорт, backfill и статистика — эндпоинты **`/dev/TracksStats`**, **`/dev/ExportTracks`**, **`/dev/BackfillTracks`** (см. раздел **«Разработка и отладка»**).

### Трекеры (блоки в конфиге)

Для каждого трекера можно задать следующие параметры:

| Параметр | Описание | Пример |
| ---------- | ---------- | -------- |
| `host` | Основной URL трекера | `https://rutracker.org` |
| `alias` | Альтернативный URL (например, .onion адрес) | `http://rutracker....onion` |
| `useproxy` | Использовать прокси для этого трекера | `true` / `false` |
| `reqMinute` | Максимальное число запросов в минуту | `8` |
| `parseDelay` | Задержка между запросами при парсинге, мс | `7000` |
| `log` | Включить логи парсера для этого трекера (Data/log/{tracker}.log) | `true` |
| `login` | Учётные данные (u — username, p — password) | `{u: "user", p: "pass"}` |
| `cookie` | Cookie для аутентификации | `"session=value"` |

Полный список трекеров и значения по умолчанию — в **`Data/example.yaml`** / **`Data/example.conf`**.

### Прокси

Настройки прокси позволяют маршрутизировать запросы через прокси-серверы.

#### Общие настройки прокси (`proxy`)

Используются для всех запросов, если не переопределены в `globalproxy`:

| Параметр | Описание | Пример |
| ---------- | ---------- | -------- |
| `pattern` | Регулярное выражение для сопоставления URL | `"\\.onion"` |
| `list` | Список прокси-серверов | `["socks5://127.0.0.1:9050"]` |
| `useAuth` | Использовать аутентификацию | `true` / `false` |
| `username` | Имя пользователя для прокси | `"user"` |
| `password` | Пароль для прокси | `"pass"` |
| `BypassOnLocal` | Обходить прокси для локальных адресов | `true` / `false` |

#### Глобальные правила прокси (`globalproxy`)

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

### Пример минимального конфига (YAML)

```yaml
listenport: 9120
syncapi: https://jacred.example.com

search:
  mergeV1: auto
  skipCatFilter: true

torznab:
  enable: true

NNMClub:
  alias: http://nnmclub....onion

globalproxy:
  - pattern: "\\.onion"
    list:
      - socks5://127.0.0.1:9050
```

Эквивалент в JSON (`init.conf`):

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
    "skipCatFilter": true
  },
  "torznab": {
    "enable": true,
    "enrichTitles": true
  }
}
```

#### Combined search (`search`)

Настройки поиска для **`/api/v2.0/indexers/.../results`** (Lampa, Jackett JSON) и Torznab XML (те же `SearchCombinedAsync`).

| Параметр | Описание | По умолчанию |
| -------- | -------- | ------------ |
| `mergeV1` | Fuzzy v1-merge: `false` / `auto` / `true` | `auto` |
| `maxV1Pairs` | Лимит v1-запросов при `mergeV1=auto` (fuzzy) | `4` |
| `v1Sort` | Сортировка v1 (`sid` = seeders; также IMDB/KP) | `sid` |
| `stripTrailingYear` | Доп. вариант fuzzy-запроса без года | `true` |
| `skipCatFilter` | Не фильтровать по `cat` / `Category[]` на сервере | `true` |

**`mergeV1: auto`** — v1 fuzzy **только в fuzzy mode** (Torznab text search, Lampa global search). Card mode (Lampa: `title` + `title_original`) — только v2 exact, без v1 fuzzy.

| `mergeV1` | Card (Lampa карточка) | Fuzzy (Query / Torznab) |
|-----------|----------------------|-------------------------|
| `false` | v2 only | v2 only |
| `auto` | v2 only | v2 + v1 fuzzy (до `maxV1Pairs`) |
| `true` | v2 + v1 fuzzy (без лимита) | v2 + v1 fuzzy (без лимита) |

IMDB/KP (`tt…`, `kp…`) всегда через v1 exact, независимо от `mergeV1`.

Jackett JSON (`/api/v2.0/indexers/.../results`) **всегда** использует combined search; на `torznab.enable` не зависит.

#### Torznab XML (`torznab`)

| Параметр | Описание | По умолчанию |
| -------- | -------- | ------------ |
| `enable` | Torznab XML и Prowlarr/Jackett Torznab-алиасы | `true` |
| `enrichTitles` | Озвучки в Torznab `<title>` | `true` |

При `enable: false` Torznab XML-эндпоинты и Prowlarr meta (`/api/v1/indexer`, `/api/v1/search`) отвечают **404**. Jackett JSON для Lampa продолжает работать.

| URL | Назначение |
|-----|------------|
| **`GET /torznab/api`** | Основной Torznab endpoint (`t=caps`, `search`, `tvsearch`, `moviesearch`, `indexers`) |
| **`GET /api/v2.0/indexers/{id}/results/torznab/api`** | Jackett-совместимый путь (алиас, тот же обработчик) |
| **`GET /api/v1/indexer/{id}/newznab`** | Prowlarr-совместимый путь (алиас, тот же обработчик) |
| **`GET /api/v1/search`** | Prowlarr Search Feed (JSON releases) |

**Клиент → URL → формат**

| Клиент | URL (относительно `http://host:9117`) | Формат |
|--------|----------------------------------------|--------|
| **Lampa** | `/api/v2.0/indexers/all/results` | Jackett JSON (`Results[]`). Тип парсера в Lampa: **Jackett**, не Prowlarr/Torznab |
| **Sonarr / Radarr** | `/torznab/api` | Torznab XML (Generic Torznab indexer) |
| **Prowlarr** (ручная настройка Generic Torznab) | `/torznab/api` | Torznab XML |
| **qui / autobrr** (discover, backend=**jackett**) | `/api/v2.0/indexers/all/results/torznab/api` | Torznab XML + `t=indexers` discover |
| **qui / autobrr** (discover, backend=**prowlarr**) | `/api/v1/indexer` + `/api/v1/indexer/1/newznab` (+ `/api/v1/search`) | Prowlarr REST + Torznab XML / Search JSON |
| **JacRed native API** | `/api/v1.0/torrents` | Собственный JSON API (не Torznab, не Jackett) |

В ответе `t=caps` поле `<server url="...">` и `<atom:link rel="self">` в RSS указывают на **фактический путь запроса** (например Jackett- или Prowlarr-алиас), а не всегда на `/torznab/api`.

**Sonarr / Radarr / Prowlarr (Generic Torznab):**

```
http://jacred:9117/torznab/api
```

API key — значение `apikey` из конфига (query `?apikey=...` или заголовок `X-Api-Key`).

---

## Источники (трекеры)

**Активные (парсинг и/или синхронизация):**  
Kinozal, NNMClub, Rutor, TorrentBy, Bitru (в т.ч. Bitru API), Rutracker, Megapeer, Selezen, Toloka, Mazepa, Baibako, Lostfilm, Animelayer.

**Отключены по умолчанию (только sync, без парсинга):**  
Anifilm, AniLibria, HDRezka.

Список для `synctrackers` и настройки по трекерам см. в **`Data/example.yaml`**.

---

## Самостоятельный парсинг

Для самостоятельного парсинга трекеров:

1. Настроить **`init.yaml`** или **`init.conf`** (примеры в **`Data/example.yaml`**, **`Data/example.conf`**).
   - Убедитесь, что для нужных трекеров указаны правильные `host`, `login` (если требуется) или `cookie`.
   - Настройте прокси, если требуется доступ к .onion доменам.

2. Выберите режим работы:
   - **Парсинг через cron:** По умолчанию база скачивается при установке, парсинг выполняется по расписанию из **`Data/crontab`**. Активируйте: `crontab /opt/jacred/Data/crontab`
   - **Синхронизация:** Укажите **`syncapi`** в конфиге, чтобы подтягивать базу с удалённого сервера. Включите `opensync: true` для участия в синхронизации.
   - **Docker:** в образе нет cron — расписание выносится на хост, отдельный контейнер или оркестратор; см. раздел **«Docker → Самостоятельный парсинг и расписание (cron) в Docker»**.

3. **Важно:** В crontab по умолчанию используется порт **9117** — при смене порта измените URL в crontab. Если в конфиге задан **`apikey`** / **`devkey`**, добавьте их в `curl` к **`/cron/*`** и **`/jsondb/save`** (см. [Безопасность](#безопасность-и-доступ-к-api)).

4. Мониторинг парсинга:
   - Логи парсеров: `Data/log/{tracker}.log` (по умолчанию `logParsers: true`, per-tracker `log: true`)
   - Логи БД: `Data/log/fdb.*.log` (по умолчанию `logFdb: true`)
   - Статистика: `GET /stats/*` (если `openstats: true`)

---

## Доступ к доменам .onion

1. Запустить Tor на порту 9050.
2. В конфиге задать для трекера **`alias`** с .onion-адресом и в **`globalproxy`** правило с `pattern: "\\.onion"` и `list: ["socks5://127.0.0.1:9050"]` (как в примере выше).

---

## Безопасность и доступ к API

JacRed использует единый слой доступа: **`UseJacRedSecurity()`** (`SecurityHeadersMiddleware` + `JacRedAuthorizationMiddleware`). Политика определяется **только** по префиксу пути в `JacRedEndpointRegistry` — без атрибутов на контроллерах.

**Сеть:** **Client IP** — после `X-Forwarded-For`; **Peer IP** — прямое TCP-подключение к Kestrel (cloudflared/nginx на том же хосте). См. `ClientNetworkContext`.

### Политики

| Политика | Правило | Ключи |
| -------- | ------- | ----- |
| **Public** | Всегда разрешено (middleware) | — |
| **ConfigApi** | LAN-клиент **или** valid `devkey` | `X-Dev-Key`, `?devkey=` |
| **DevAdmin** | LAN-клиент **или** valid `devkey` | `X-Dev-Key`, `?devkey=` |
| **ApiKeyWhenConfigured** | Если `apikey` задан — требуется valid key; иначе открыто | `?apikey=`, `X-Api-Key`, `Bearer` |

**Коды отказа:** `OPTIONS` → 204; ключ настроен, но не передан → **401**; иначе → **403**.

> **ConfigApi = DevAdmin** по сети: same-host reverse proxy **сам по себе не заменяет** `devkey`. Нужен LAN-клиент (RFC1918 / loopback по Client IP) или заголовок/`?devkey=`.

### Префиксы путей → политика

| Префикс | Политика | Доп. проверка в контроллере |
| ------- | -------- | ---------------------------- |
| `/dev/`, `/cron/`, `/jsondb` | DevAdmin | — |
| `/api/v1.0/config` | ConfigApi | — |
| `/`, `/stats`, `/settings` | Public | HTML-оболочки |
| `/health`, `/version`, `/lastupdatedb`, `/api/v1.0/conf` | Public | — |
| `/sync/*` | Public | `opensync` для данных sync |
| `/swagger`, `/openapi.yaml`, статика `/css/` … | Public | `web: true` для UI |
| **Всё остальное** | ApiKeyWhenConfigured | `openstats` для `/stats/*` JSON |

### Доступ по контексту клиента

| Политика | Loopback / LAN (Client IP) | Same-host proxy без devkey | Интернет / удалённый прокси |
| -------- | -------------------------- | -------------------------- | --------------------------- |
| Public | ✓ | ✓ | ✓ |
| ConfigApi | ✓ | ✗ | `devkey` |
| DevAdmin | ✓ | ✗ | `devkey` (если задан в конфиге) |
| ApiKeyWhenConfigured | `apikey` если задан | `apikey` если задан | `apikey` если задан |

### Белый список без `apikey`

Если в конфиге задан `apikey`, следующие пути **не требуют** его на уровне middleware:

`/`, `/stats`, `/settings`, `/health`, `/version`, `/lastupdatedb`, `/openapi.yaml`, `/swagger`, `/api/v1.0/conf`, `/sync/*`

**Не входят:** `/cron/*`, `/dev/*`, `/jsondb/*`, `/api/v1.0/config/*`, поиск, Torznab, `/stats/torrents` и др.

### Ключи: `apikey` vs `devkey`

| Ключ | Назначение | Не заменяет |
| ---- | ---------- | ----------- |
| `apikey` | Lampa, Sonarr, Prowlarr, публичный API | `devkey` для `/cron/*` |
| `devkey` | Админ: cron, dev, jsondb, config API извне | `apikey` для поиска |

Пример cron при обоих ключах:

```bash
curl -s -H "X-Api-Key: YOUR_API_KEY" -H "X-Dev-Key: YOUR_DEV_KEY" \
  "http://127.0.0.1:9117/cron/rutor/parse"
```

### Основные маршруты (краткая трассировка)

| Маршрут | Политика | Вторичный gate |
| ------- | -------- | -------------- |
| `GET /api/v2.0/indexers/.../results` | ApiKeyWhenConfigured | — |
| `GET /torznab/api` | ApiKeyWhenConfigured | — |
| `GET /api/v1.0/torrents` | ApiKeyWhenConfigured | — |
| `GET /stats/torrents`, `/stats/tracks`, `/stats/meta` | ApiKeyWhenConfigured | `openstats` |
| `GET /sync/fdb/torrents` | Public | `opensync` |
| `GET/POST /api/v1.0/config/*` | ConfigApi | — |
| `GET /cron/{tracker}/parse` | DevAdmin | — |
| `GET /jsondb/save` | DevAdmin | — |

### Матрица доступа

Полная трассировка маршрутов, политик и вторичных проверок — [`AccessTraceabilityMatrix.md`](AccessTraceabilityMatrix.md). Источник истины в коде: `Infrastructure/Security/JacRedEndpointRegistry.cs`.

---

## API

### OpenAPI / Swagger

| URL | Назначение |
|-----|------------|
| `GET /swagger` | Swagger UI (интерактивная документация) |
| `GET /swagger/v1/swagger.json` | OpenAPI 3.0 JSON (конвертируется из `wwwroot/openapi.yaml`) |
| `GET /openapi.yaml` | Статическая OpenAPI 3.0 YAML (`wwwroot/openapi.yaml`) |

Swagger UI по умолчанию загружает **`/openapi.yaml`**; в выпадающем списке также доступен JSON (`/swagger/v1/swagger.json`).

При настроенном `apikey` пути `/swagger`, `/swagger/*` и `/openapi.yaml` доступны без ключа (как `/health`). Схемы авторизации в UI: `apikey` (query), `X-Api-Key`, `Authorization: Bearer`, `X-Dev-Key` (для Config API).

В спецификацию входят публичные эндпоинты (`/api/*`, `/torznab/*`, `/stats/*`, `/sync/*`, `/health`, …). Пути `/cron/*`, `/dev/*`, `/jsondb/*` в OpenAPI **не описаны** (политика DevAdmin).

Проверка соответствия маршрутов политикам: [`AccessTraceabilityMatrix.md`](AccessTraceabilityMatrix.md).

### Основные эндпоинты

- **`GET /`** — веб-интерфейс поиска (если `web: true`).
- **`GET /stats`** — страница статистики (если `web: true`; данные — `/stats/torrents`, `/stats/meta`).
- **`GET /settings`** — веб-редактор конфигурации (если `web: true`; см. **«Config API»** ниже).
- **`GET /health`** — проверка работы. Ответ JSON: `{"status":"OK"}`.
- **`GET /version`** — версия приложения. Ответ JSON: `{"version":"1.0.0"}`.
- **`GET /lastupdatedb`** — дата/время последнего обновления БД (UTC). Ответ JSON: `{"lastupdatedb":"dd.MM.yyyy HH:mm"}`.

### API поиска

Сводная таблица «клиент → URL → формат» — в разделе **Torznab / Jackett** выше.

- **`GET /api/v2.0/indexers/{status}/results`** — поиск в формате Jackett JSON (**Lampa** и др.).
  - Combined search (`search.*`): v2 card/fuzzy + v1 fuzzy (только fuzzy mode при `mergeV1: auto`) + IMDB/KP exact + card fallback.
  - Параметры Lampa: `Query`, `title`, `title_original`, `year`, `is_serial`, `genres`, `Category[]`, `Tracker[]`, `season`, `ep`, `limit`, `offset`, `apikey`.
  - Ответ: `{ "Results": [...], "jacred": true }` с `ffprobe`, `languages`, `info` при `tracks: true`.
- **`GET /api/v2.0/indexers`** — список индексаторов (Jackett/Prowlarr).
- **`GET /api/v1/indexer`** — список индексаторов в формате Prowlarr REST API (qui/autobrr discover fallback).
- **`GET /api/v1/indexer/{id}`** — детали индексатора Prowlarr (`id=1`, для qui backend=prowlarr).
- **`GET /api/v1/indexer/{id}/newznab`** — Torznab XML через Prowlarr-совместимый путь (`t=caps|search|…`).
- **`GET /api/v1/search`** — Prowlarr Search Feed ([wiki](https://wiki.servarr.com/en/prowlarr/search#search-feed)): JSON-массив релизов.
  - Параметры: `query`, `type` (`search`|`tvsearch`|`movie`|`music`|`book`), `indexerIds` (`1`, `-2` torrents; `-1` usenet → пусто), `categories`, `limit`, `offset`, `apikey`.
  - Brace-токены в `query` (как в UI Prowlarr): `{ImdbId:tt…}`, `{Season:1}`, `{Episode:2}` и т.п.
  - Один агрегированный indexer `id=1`; ответ в схеме ReleaseResource (`guid`, `title`, `size`, `seeders`, `magnetUrl`, `categories`, …).
  - JacRed-расширения как у Jackett: `ffprobe`, `languages`, `info` при `tracks: true` (иначе поля опускаются / null).
- **`GET /torznab/api`** — Torznab XML, основной endpoint (`t=search|tvsearch|moviesearch|caps|indexers`).
- **`GET /api/v2.0/indexers/{id}/results/torznab/api`** — Torznab XML (Jackett-алиас, тот же обработчик).

  Параметры и поведение одинаковы для обоих Torznab-путей:
  - Параметры: `q`, `imdbid`, `season`, `ep`, `year`, `cat`, `title`, `title_original`, `is_serial`, `limit`, `offset`, `apikey`.
  - IMDB/KP ID (`tt…`, `kp…`) → поиск через v1 с `exact=true`.
  - Card mode (Lampa): `title` + `title_original` + `year` + `is_serial` + `genres`.
  - Объединение v1+v2, bilingual `Русский / English`, post-filter по сезону/эпизоду/году/категории.
- **`GET /api/v1.0/torrents`** — поиск торрентов (собственный JSON API JacRed, не Torznab и не Jackett).
  - Параметры: `query` (поисковый запрос), `tracker` (трекер), `category` (категория), `quality` (качество).
- **`GET /api/v1.0/qualitys`** — список доступных качеств.

### Управление конфигурацией (Config API)

REST API и страница **`/settings`** для редактирования **`init.yaml`** / **`init.conf`**.

**Доступ:** политика **ConfigApi** — LAN-клиент **или** `devkey`. Same-host reverse proxy без devkey **недостаточен**. При заданном `apikey` — также ключ API для путей вне белого списка.

| Метод | Путь | Описание |
|-------|------|----------|
| `GET` | `/api/v1.0/config` | Текущий конфиг (`data` + `content`, метаданные файла) |
| `GET` | `/api/v1.0/config/schema` | Схема полей для формы настроек |
| `POST` | `/api/v1.0/config/validate` | Валидация без записи на диск |
| `POST` | `/api/v1.0/config/diff` | Diff с текущим конфигом (перед сохранением) |
| `POST` | `/api/v1.0/config/render` | Объект формы → YAML/JSON текст |
| `POST` | `/api/v1.0/config/parse` | YAML/JSON текст → объект |
| `POST` | `/api/v1.0/config/format` | Нормализация и форматирование |
| `POST` | `/api/v1.0/config` | Сохранение (атомарная запись; hot-reload ~10 с) |

Тело запросов: `{ "data": { ... } }` (форма) и/или `{ "content": "...", "format": "yaml" }` (текстовый редактор). Подробности — в **`/openapi.yaml`**.

### Прочее управление

- **`GET /api/v1.0/conf`** — проверка apikey (`?apikey=...`).
- **`GET /jsondb/save`** — сохранить БД на диск (при использовании syncapi скрипт установки не вызывает save; при собственном парсинге cron вызывает save по расписанию).
  - Доступ: политика **DevAdmin** — LAN или `devkey`; при `apikey` — также ключ для middleware (см. [Безопасность](#безопасность-и-доступ-к-api)).

### Разработка и отладка

- **`GET /dev/*`** — инструменты разработки и отладки БД.
  - Доступ: политика **DevAdmin** — LAN или `devkey` (см. [Безопасность](#безопасность-и-доступ-к-api)).

| Эндпоинт | Описание |
| --------- | --------- |
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

### Статистика и синхронизация

**Сводки (для UI `/stats` и API):**

| Эндпоинт | Ответ |
|----------|--------|
| `GET /stats/torrents` | Массив из `stats.json` |
| `GET /stats/tracks` | `{ ok, updatedAt, fromCache, stats }` из `tracks-stats.json` |
| `GET /stats/meta` | `{ ok, updatedAt, updatedAtLocal, tracksStatsUpdatedAt }` |

- Force refresh tracks: **`GET /dev/TracksStats?refresh=true`**
- **`GET /sync/*`** — эндпоинты синхронизации (если `opensync: true`).
  - **`GET /sync/fdb/torrents`** — основной протокол синхронизации (collections + pagination).

### Парсинг трекеров

- **`GET /cron/{tracker}/parse`** — запуск парсинга трекера.
- **`GET /cron/{tracker}/ParseAllTask`** — парсинг всех задач трекера.
- **`GET /cron/{tracker}/UpdateTasksParse`** — обновление задач парсинга.
- **`GET /cron/{tracker}/parseMagnet`** — парсинг магнет-ссылок (для поддерживающих трекеров).
- Дополнительные параметры: `parseFrom`, `parseTo`, `parseFromDate` (зависит от трекера).

**Доступ:** политика **DevAdmin** (`/cron/*`). Подробные таблицы LAN / tunnel / ключи — в разделе **[Безопасность и доступ к API](#безопасность-и-доступ-к-api)**.

HTTP-вызовы `/cron/*` логируются с префиксом `cron:` (уровень зависит от `logging.cronSkipFastMs`).

**Пример `curl` при включённых `apikey` и `devkey`:**

```bash
curl -s -H "X-Api-Key: YOUR_API_KEY" -H "X-Dev-Key: YOUR_DEV_KEY" \
  "http://127.0.0.1:9117/cron/rutor/parse"
```

---

## Сборка

### Требования для сборки

- **.NET 10.0 SDK** (см. **`JacRed.csproj`**)
- **Git** (для генерации версии из тегов)
- **Bash** (для скрипта сборки)

### Сборка для текущей платформы

```bash
./build.sh
```

### Сборка для всех платформ

```bash
./build.sh --all
```

Поддерживаемые платформы:

- **Linux**: amd64, arm64
- **Windows**: x64
- **macOS**: arm64, amd64

Результат сборки находится в каталоге **`dist/<platform>/`** (self-contained).

### Особенности сборки

- **Linux / Windows:** single-file публикация (один исполняемый файл), сжатие включено
- **macOS (osx-arm64, osx-amd64):** каталог с бинарником и зависимостями (`PublishSingleFile=false`) — обход известного бага .NET с `EnableCompressionInSingleFile` на Apple Silicon
- Self-contained (включает .NET runtime)
- Оптимизация для скорости выполнения
- Версия генерируется автоматически из Git тегов через `generate-version.sh`

---

## Docker

Образ можно запускать через **Docker** или **Docker Compose**. Конфигурация (`init.yaml` или `init.conf`) и данные (база fdb, логи) хранятся в томах или bind-монтированных каталогах. При первом запуске конфиг по умолчанию копируется автоматически (поддерживаются и named volumes, и bind mounts).

### Docker Run

```bash
docker run -d \
  --name jacred \
  -p 9117:9117 \
  -v jacred-config:/app/config \
  -v jacred-data:/app/Data \
  --restart unless-stopped \
  ghcr.io/jacred-fdb/jacred:latest
```

### Docker Compose

**Вариант с named volumes** (рекомендуется):

```yaml
name: jacred

services:
  jacred:
    image: ghcr.io/jacred-fdb/jacred:latest
    container_name: jacred
    restart: unless-stopped
    ports:
      - "9117:9117"
    volumes:
      - jacred-config:/app/config
      - jacred-data:/app/Data
    environment:
      - TZ=Europe/London
      - UMASK=0027
    healthcheck:
      test: ["CMD", "curl", "-f", "-s", "--max-time", "10", "http://127.0.0.1:9117/health"]
      interval: 30s
      timeout: 15s
      retries: 3
      start_period: 45s
    deploy:
      resources:
        limits:
          memory: 2048M

volumes:
  jacred-config:
  jacred-data:
```

**Вариант с bind mounts** (удобно для доступа к файлам на хосте) — замените блок `volumes` в сервисе на:

```yaml
volumes:
  - ./config:/app/config
  - ./data:/app/Data
```

Готовые примеры: **`docker/docker-compose.yml`** (bind mounts), **`docker-compose.example.yml`** (named volumes).

**Полезно:**

- **Конфиг:** после первого запуска настройте **`init.yaml`** или **`init.conf`** в томе `jacred-config` или каталоге `./config` (при bind mount). Конфиг автоматически копируется из `/app/config/` в `/app/` при старте контейнера.
- **Порты:** веб-интерфейс и API доступны на порту **9117** (при необходимости измените маппинг `ports` и `listenport` в конфиге).
- **Память:** при большой базе или активном парсинге увеличьте лимит `memory` в `deploy.resources.limits` (рекомендуется минимум 2GB).
- **Тома:**
  - `jacred-config` — хранит конфигурацию (`init.yaml` или `init.conf`)
  - `jacred-data` — хранит базу данных (`fdb/`), логи (`log/`), временные файлы (`temp/`) и треки (`tracks/`)
- **Healthcheck:** контейнер включает встроенный healthcheck, проверяющий доступность `/health` эндпоинта.
- **Сборка своего образа:** в корне репозитория выполните `docker build -t jacred .` и в примерах выше замените образ на `jacred:latest`.
- **Переменные окружения:** поддерживаются `TZ` (часовой пояс) и `UMASK` (права на файлы, по умолчанию `0027`).

### Самостоятельный парсинг и расписание (cron) в Docker

В образе **нет** планировщика **cron** (и **нет** установки заданий в crontab внутри контейнера). Фоновые циклы приложения (синхронизация по `syncapi`, статистика и т.д.) работают сами; **периодический вызов HTTP-эндпоинтов** `/cron/...` и **`/jsondb/save`** нужно организовать **снаружи** контейнера.

**Типовые варианты:**

1. **Cron на хосте** (чаще всего) — в crontab пользователя на машине, где крутится Docker, вызывать `curl` на опубликованный порт (например `http://127.0.0.1:9117/...`). Запрос с хоста в контейнер обычно приходит с адреса из **приватной подсети** (в т.ч. шлюз Docker `172.x`), что удовлетворяет проверке «локальная/приватная сеть» в приложении.
2. **Отдельный контейнер с cron** — маленький образ (например `curl` + `cron`), в том же Docker Compose, который по расписанию дергает сервис JacRed по **внутреннему** имени и порту (например `http://jacred:9117/...`). Убедитесь, что с точки зрения JacRed IP источника остаётся в приватном диапазоне (типично так и есть в user-defined bridge-сети).
3. **Kubernetes CronJob**, **systemd timer** на хосте — по сути то же, что п.1: периодический HTTP-запрос к JacRed.

**Ориентир по расписанию:** в репозитории лежит пример **`Data/crontab`** (парсинг по трекерам и `*/5 * * * *` для **`/jsondb/save`**). Скопируйте нужные строки в свой crontab на хосте (или в свой шаблон для контейнера с cron) и:

- замените хост/порт на ваши (`127.0.0.1:9117` или имя сервиса в Compose);
- если в **`init.yaml` / `init.conf`** задан **`apikey`** — добавьте в каждый `curl` ключ (`-H "X-Api-Key: ..."` или `?apikey=...`), иначе запросы к `/cron/*` и `/jsondb/save` получат **401**;
- если задан **`devkey`** и запрос считается «локальным» — добавьте `-H "X-Dev-Key: ..."` или `?devkey=...`.

Подробнее про ключи для `/cron/*` — в разделе **«Парсинг трекеров»** выше.

**Синхронизация вместо своего парсинга:** можно указать **`syncapi`** и не вызывать `/cron/*` вовсе; тогда достаточно конфигурации и встроенных циклов приложения (плюс при необходимости **`/jsondb/save`** по расписанию, если вы ведёте локальную запись БД).

---

## Роутер (Cloudflare Worker)

В каталоге **`router/`** находится Cloudflare Worker для маршрутизации запросов по хосту/пути на разные бэкенды (домашний сервер, Tailscale, туннели, Pages, Vercel) с кешированием и заголовками.

**Возможности:**

- Маршрутизация по хосту, пути и query-параметрам
- Поддержка нескольких типов источников
- Индивидуальные заголовки и политики кэширования для каждого маршрута
- Перезапись пути (path rewriting)
- Подстановочные символы в хосте и пути

Документация и настройка — в **`router/README.md`**.

---

## Решение проблем

### Приложение не запускается

- **Ошибка «Couldn't find a valid ICU package»** — .NET требует библиотеку ICU на Linux. Установите: `apt install libicu-dev` (Debian/Ubuntu) или `libicu76` / `libicu72` (имя зависит от версии). Проверьте доступные пакеты: `apt-cache search libicu`. Подробнее: [aka.ms/dotnet-missing-libicu](https://aka.ms/dotnet-missing-libicu)
- Проверьте наличие конфигурационного файла (`init.yaml` или `init.conf`)
- Убедитесь, что порт не занят другим процессом: `netstat -tuln | grep 9117`
- Проверьте логи systemd: `journalctl -u jacred -f`
- Для Docker: проверьте логи контейнера: `docker logs jacred`

### База данных не обновляется

- Проверьте, что cron настроен правильно: `crontab -l` (на **хосте** или в отдельном контейнере с планировщиком; **внутри** образа JacRed cron нет)
- Для Docker: убедитесь, что по расписанию вызываются **`/cron/...`** и при необходимости **`/jsondb/save`**, с учётом **`apikey`** / **`devkey`** в `curl`, если они заданы в конфиге
- Убедитесь, что `syncapi` указан корректно (если используется синхронизация)
- Проверьте логи парсеров: `tail -f Data/log/{tracker}.log`
- Убедитесь, что трекер доступен и учётные данные верны

### API не отвечает

- Проверьте, что приложение запущено: `systemctl status jacred`
- Проверьте health endpoint: `curl http://localhost:9117/health`
- Убедитесь, что `apikey` указан правильно (если используется авторизация)
- Проверьте настройки `listenip` и `listenport` в конфиге

### Проблемы с прокси/Tor

- Убедитесь, что Tor запущен на порту 9050: `netstat -tuln | grep 9050`
- Проверьте правильность регулярного выражения в `globalproxy.pattern`
- Убедитесь, что формат прокси корректен: `socks5://127.0.0.1:9050`
- Проверьте логи для ошибок подключения

### Высокое потребление памяти

- Включите `evercache` для оптимизации работы с файлами
- Уменьшите `maxreadfile` в конфиге
- Настройте ротацию логов через `logFdbRetentionDays`, `logFdbMaxSizeMb`, `logFdbMaxFiles`
- Для Docker: увеличьте лимит памяти в `deploy.resources.limits.memory`

---

## Архитектура

JacRed — **ASP.NET Core 10** (single project `JacRed.csproj`):

```
Controllers/          → HTTP (тонкий слой)
Application/          → поиск, индекс, dev-сервисы
Infrastructure/       → FileDB, трекеры, security, logging, workers
Configuration/        → init.yaml / hot-reload
Models/               → DTO и контракты API
```

### Основные компоненты

| Компонент | Путь | Назначение |
| --------- | ---- | ---------- |
| **Security** | `Infrastructure/Security/` | `JacRedEndpointRegistry`, `JacRedAuthorizationMiddleware`, `UseJacRedSecurity()` |
| **Logging** | `Infrastructure/Logging/` | `JacRedLog`, console categories, M.E.Logging |
| **FileDB** | `Infrastructure/Persistence/FileDB/` | Файловая БД, `masterDb`, cron fdb |
| **Search** | `Infrastructure/Indexers/`, `Application/Search/` | Jackett / Torznab / v1 torrents |
| **Trackers** | `Infrastructure/Trackers/{Name}/` | Parser + SyncService на трекер |
| **Background** | `Infrastructure/Background/` | `SyncWorker`, `StatsWorker`, `TrackersWorker`, `FileDbWorker`, `TracksWorker`, `FastDbRefreshWorker` |
| **Config** | `Configuration/AppConfigurationProvider.cs` | Загрузка, hot-reload, redaction |

### Фоновые процессы

- **SyncCron** — pull с `syncapi` (`/sync/fdb/torrents`)
- **TrackersCron** — парсинг по HTTP `/cron/*` (внешний cron) + внутренние циклы
- **StatsCron** — `stats.json`, `tracks-stats.json`
- **TracksCron** — ffprobe через `tsuri` (если `tracks: true`)
- **FileDB cron** — evercache, ffprobe refresh

---

## Лицензия

MIT License. См. файл [LICENSE](LICENSE) для подробностей.
