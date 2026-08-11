# JacRed

![Jacred — A Torrent aggregator & file database](web/public/img/jacred-social-preview.png)

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
- 📖 **OpenAPI / Swagger** — `/openapi.yaml` (v1.2.0), интерактивная документация на `/swagger`
- 🗂️ **23 трекера** — парсинг и sync (см. [Источники](#источники-трекеры))
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
| `devkey` | Ключ для `/dev/`, `/cron/`, `/jsondb/*`, `/api/v1.0/config/*` из интернета или через туннель. **LAN-клиент** или **`devkey`** (`X-Dev-Key`, `?devkey=`). Reverse proxy (loopback или Docker + XFF) **без** devkey **не открывает** admin/config | — |
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
| `synctrackers` | Фильтр трекеров при pull (slug’и из `TrackerSlug` / `ConfigSchema.KnownTrackerSlugs`) | см. example |
| `disable_trackers` | Исключить трекеры из sync и API `GET /api/v1.0/trackers` | `[]` |
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
| `tracksconcurrency` | Макс. параллельных анализов к TorrServer (глобально для всех typetask) | `2` |
| `tracksffptimeout` | Таймаут HTTP `/ffp` при `sid > 0`, сек | `60` |
| `tracksffptimeoutnosid` | Короткий таймаут `/ffp`: `sid == 0` или нет сидов при уже идущей загрузке (`bytes_read > 0`), сек | `30` |
| `tracksreadtimeout` | Ожидание `file_stats` от TorrServer перед `/ffp`, сек | `30` |
| `trackspeerwaittimeout` | Бюджет на проверку сидов/`bytes_read` и на ожидание буфера перед `/ffp`, сек | `30` |
| `tracksffpretry` | Доп. file id для `/ffp` за одну попытку (pack-торренты) | `2` |
| `tracksminbufferkb` | Мин. буфер (`loaded_size` / `bytes_read`) перед `/ffp`, KB | `512` |
| `tracksorphansweepmin` | Интервал orphan sweep: rem торрентов в `trackscategory`, не занятых анализом, мин | `15` |
| `tracksmod` | Режим треков: 0 — все; 1 — отключает typetask **3 и 4** | `0` |
| `tracksdelay` | Пауза между стартами следующего торрента внутри очереди (±10% jitter), мс; также нижняя граница backoff после ошибок TS | `20000` |
| `tracksinterval` | Интервалы: typetask 1 → `task1`; typetask N (2–5) → `task0 + N` мин (напр. `task0: 180` → task2≈182, task5≈185) | `task1: 60, task0: 180` |
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

**Счётчики tracks (confirm/wait/skip)** в `stats.json`: `confirm` — трек есть в tracks DB (RAM / индекс / файл с непустым `streams`, как `HasTrackForTorrent`); `wait` — magnet есть, трека нет; `skip` — `ffprobe_tryingdata ≥ tracksatempt`. Поле `ffprobe` в FileDB не канонично.

Результаты анализа сохраняются в **`Data/tracks/{aa}/{b}/{hash}.json`**. Экспорт, backfill и статистика — эндпоинты **`/dev/TracksStats`**, **`/dev/ExportTracks`**, **`/dev/BackfillTracks`** (см. раздел **«Разработка и отладка»**).

#### Параллелизм tracks (`tracksconcurrency`) и TorrServer

Модуль tracks запускает **5 фоновых очередей (typetask)** по возрасту/типу торрентов плюс **отдельный orphan sweep**:

| typetask | Окно | Кого берёт |
| -------- | ---- | ---------- |
| 1 | последние сутки | свежие торренты |
| 2 | 1 день – 1 месяц | недавние |
| 3 | 1 месяц – 1 год | старые (`sid > 0`, если не typetask 1/2) |
| 4 | старше года | архив (`sid > 0`) |
| 5 | обновления | старые, но с недавним `updateTime` |
| orphan | каждые `tracksorphansweepmin` | rem торрентов в `trackscategory`, не из in-flight анализа |

При `tracksmod: 1` typetask 3 и 4 **не работают** (только «день + месяц»). Перед постановкой в очередь кандидаты **дедуплицируются по infohash**; один и тот же hash не анализируется параллельно (per-hash lock).

**`tracksconcurrency`** — глобальный лимит **одновременных** анализов через TorrServer (add → wait `file_stats` → проверка сидов → ожидание буфера ≥ `tracksminbufferkb` → выбор media file → `GET /ffp/{hash}/{id}` → rem). Все typetask делят один пул слотов: если слотов не хватает, лишние очереди **ждут** освобождения. **`tracksdelay`** задаёт паузу **между стартами** следующего торрента **внутри** каждой очереди (~20 с ±10% по умолчанию), но до `tracksconcurrency` анализов могут идти параллельно из разных typetask. После недоступности TS / ошибок API применяется backoff не короче `tracksdelay` (минимум 5–10 с).

**File id в TorrServer:** номера файлов **1-based**, сортировка по пути. Id `1` — не обязательно основное видео. JacRed выбирает кандидатов по video-расширению и размеру, исключая sample/trailer/proof/preview; если видео не найдено — fallback на крупнейший файл или id `1`. При HTTP 400 пробует до `tracksffpretry` доп. id за одну попытку. HTTP 400 **не** выставляет `ffprobe_tryingdata` в `tracksatempt` — каждая неудача даёт `+1` (включая typetask 1), пока не достигнут лимит.

**Таймауты:** мёртвые торренты (нет сидов и `bytes_read`) не держат слот долго — `/ffp` пропускается после `trackspeerwaittimeout`. Затем ещё один бюджет `trackspeerwaittimeout` на набор буфера ≥ `tracksminbufferkb`. Для `sid == 0` или «есть байты, но нет сидов» — короткий `tracksffptimeoutnosid`; при живых сидах и `sid > 0` — до `tracksffptimeout` на `/ffp`. Общий лимит одной попытки analyze (408): `tracksreadtimeout` + 2×`trackspeerwaittimeout` + `/ffp`×(1+`tracksffpretry`) + 30 с.

**Troubleshooting tracks (лог):**

| Сообщение | Значение |
| --------- | -------- |
| `нет сидов/данных — пропуск /ffp` | Мёртвый торрент, early abort |
| `нет подходящего media-файла` | Все file id вернули 400 |
| `ffp timeout` (504) | `/ffp` не успел за выбранный ffp-таймаут |
| `overall …s` (408) | Общий лимит analyze (см. формулу выше) |
| `Backoff …ms` | Пауза после down/timeout TS или API-ошибки |
| `orphan sweep: rem` | Периодическая очистка «сирот» в `trackscategory` |
| `уже анализируется — пропуск` | Per-hash lock (дубль в очереди / между typetask) |

Выбор TorrServer: из массива **`tsuri`** берётся сервер с **наименьшей** загрузкой в категории **`trackscategory`**.

**Матрица: число TorrServer × `tracksconcurrency`**

| | `tracksconcurrency = 2` | `= 3` | `= 5` |
| --- | --- | --- | --- |
| **1 TorrServer** | до 2 `/ffp` одновременно; мягко для слабого TS | до 3; баланс скорости и нагрузки | до 5; быстро, но тяжело для одного TS (ffprobe + play) |
| **2 TorrServer** | 2 слота на весь пул; часто ~1 job на TS | ~2+1 между TS | до ~2–3 на TS при полной очереди |
| **3 TorrServer** | максимум 2 TS заняты одновременно | **оптимально: по 1 job на TS** | до ~2+2+1; быстрый разбор backlog |

**Пример (3 TS, `tracksconcurrency: 3`):** typetask 1, 2 и 5 одновременно взяли кандидатов → три слота на TS-A, TS-B, TS-C; typetask 3 ждёт, пока один слот освободится после rem.

**Грубая оценка нагрузки на один TS при полной очереди:**

`нагрузка ≈ min(tracksconcurrency, активные_typetask, кол-во_tsuri) / кол-во_tsuri`

**Рекомендуемые значения:**

| Схема | `tracksconcurrency` |
| ----- | ------------------- |
| 1 TS (VPS / слабый) | **2** |
| 1 TS (выделенный, мощный) | **3** |
| 2 TS | **3** |
| 3 TS, обычный backlog | **3** (по одному job на TS) |
| 3 TS, большой backlog, TS не перегружены | **5** |

Каноническое хранение ffprobe — **`Data/tracks`**, не поле `ffprobe` в FileDB. Cron пропускает торренты, для которых трек уже есть в tracks DB (индекс / RAM / файл).

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
| `login` | Учётные данные (u — username, p — password), если трекер требует логин | `{u: "user", p: "pass"}` |
| `cookie` | Статическая cookie-сессия (часто альтернатива `login`) | `"session=value"` |

Полный список трекеров и значения по умолчанию — в **`Data/example.yaml`** / **`Data/example.conf`**.

**Аутентификация отдельных трекеров** (плейсхолдеры — в `Data/example.yaml`; реальные секреты не коммитьте):

| Трекер | Что нужно |
| ------ | --------- |
| **Korsars** | `login.u` / `login.p` **или** статическая cookie с `bb_data` (если задана cookie — логин не обязателен) |
| **Anifilm** | `login` **или** session cookie (например `XSRF-TOKEN` + session) |
| **Anistar** | Статическая cookie (`cf_clearance` + session) обязательна для live-парса; получить экспортом из браузера или через FlareSolverr вручную. **Не** использует блок `flaresolverr` / `/cron/cloudflare/Warmup` как Rutracker |
| **Anibelka** | Только анонимно — **не** задавайте `cookie` / `login` (в раздачах есть passkey) |
| **Ultradox** | Логин не нужен; Referer должен выглядеть как поиск google/yandex (свой origin → 503) |
| **Rutracker** | См. FlareSolverr ниже и [`Infrastructure/Trackers/Rutracker/README.md`](Infrastructure/Trackers/Rutracker/README.md) |
| **Baibako / Lostfilm / Animelayer / …** | См. блоки в `Data/example.yaml` |
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

Известные slug’и (`ConfigSchema.KnownTrackerSlugs` / OpenAPI `TrackerSlug`, 23 шт.):

`anibelka`, `anidub`, `anifilm`, `aniliberty`, `animelayer`, `anistar`, `baibako`, `bitru`, `kinozal`, `knaben`, `korsars`, `leproduction`, `lostfilm`, `mazepa`, `megapeer`, `nnmclub`, `rutor`, `rutracker`, `selezen`, `toloka`, `torrentby`, `ultradox`, `viruseproject`.

**Парсеры (cron + FileDB):** все slug’и выше имеют контроллер `/cron/{slug}/…` (кроме служебных `cloudflare` / `maintenance`).

**Иконки UI:** `/img/ico/{slug}.ico` (fallback `/img/ico/default.ico`). Файлы вроде `anilibria.ico` / `hdrezka.ico` / `underverse.ico` — устаревшие ассеты, **не** активные трекеры.

**Не добавляйте в `synctrackers`:** retired-трекеры (AniLibria, HDRezka и т.п.). При фильтрации остатков sync — `disable_trackers`.

Список для `synctrackers` и блоки настроек — в **`Data/example.yaml`**. Конфиг приложения при запуске: **`init.yaml`** / **`init.conf`** в **корне рабочего каталога** (рядом с бинарником), не `Data/init.yaml` (тот — шаблон/defaults при установке).

---

## Самостоятельный парсинг

Для самостоятельного парсинга трекеров:

1. Настроить **`init.yaml`** или **`init.conf`** (примеры в **`Data/example.yaml`**, **`Data/example.conf`**).
   - Убедитесь, что для нужных трекеров указаны правильные `host`, `login` / `cookie` (см. таблицу аутентификации выше).
   - Добавьте slug’и в **`synctrackers`**, если хотите видеть их в `GET /api/v1.0/trackers` и sync-фильтре.
   - Настройте прокси, если требуется доступ к .onion доменам.
   - **Rutracker / Cloudflare:** блок **`flaresolverr`** + на VPS egress через **WARP SOCKS** (`PROXY_URL` у контейнера FlareSolverr, volume для `/var/lib/cloudflare-warp`). Cookie `cf_clearance` живёт в persistent-сессии FlareSolverr — держите `sessionIdleMinutes` и keep-alive Warmup. `network_mode: host` сам IP не меняет. Альтернатива без FlareSolverr — Worker **`Rutracker.alias`**. Подробности: [`Infrastructure/Trackers/Rutracker/README.md`](Infrastructure/Trackers/Rutracker/README.md).
   - **Anistar:** задайте cookie в конфиге; встроенный FlareSolverr-warmup Rutracker на Anistar не действует.

2. Выберите режим работы:
   - **Парсинг через cron:** По умолчанию база скачивается при установке, парсинг выполняется по расписанию из **`Data/crontab`** (включая `cloudflare-warmup` за ~5 мин до `rutracker-parse`, daily page-парсеры и hourly Rutor-style для anibelka/korsars/ultradox). Активируйте: `crontab /opt/jacred/Data/crontab`
   - **Синхронизация:** Укажите **`syncapi`** в конфиге, чтобы подтягивать базу с удалённого сервера. Включите `opensync: true` для участия в синхронизации.
   - **Docker:** в образе нет cron — расписание выносится на хост, отдельный контейнер или оркестратор; см. раздел **«Docker → Самостоятельный парсинг и расписание (cron) в Docker»**.

3. **Важно:** В crontab по умолчанию используется порт **9117** — при смене порта измените URL в строках **`Data/crontab`**. Если в конфиге задан **`apikey`** / **`devkey`**, добавьте их в URL (`?apikey=...` / `?devkey=...`) в каждой строке crontab (см. [Безопасность](#безопасность-и-доступ-к-api)). Задания вызывают [`Data/run-job.sh`](Data/run-job.sh) (`flock` + `curl --max-time`).

4. Мониторинг парсинга:
   - Логи парсеров: `Data/log/{tracker}.log` (по умолчанию `logParsers: true`, per-tracker `log: true`)
   - Логи БД: `Data/log/fdb.*.log` (по умолчанию `logFdb: true`)
   - Активные длинные джобы: `GET /health/background-jobs` (ParseAll / UpdateTasks; page-only парсеры туда обычно не попадают)
   - Статистика: `GET /stats/*` (если `openstats: true`)

---

## Доступ к доменам .onion

1. Запустить Tor на порту 9050.
2. В конфиге задать для трекера **`alias`** с .onion-адресом и в **`globalproxy`** правило с `pattern: "\\.onion"` и `list: ["socks5://127.0.0.1:9050"]` (как в примере выше).

---

## Безопасность и доступ к API

JacRed использует единый слой доступа: **`UseJacRedSecurity()`** (`SecurityHeadersMiddleware` + `JacRedAuthorizationMiddleware`). Политика определяется **только** по префиксу пути в `JacRedEndpointRegistry` — без атрибутов на контроллерах.

**Сеть:** **Peer IP** — прямое TCP-подключение к Kestrel. **Client IP** из `CF-Connecting-IP` / `X-Real-IP` / `X-Forwarded-For` учитывается **только** если peer — loopback (cloudflared/nginx на том же хосте); иначе Client IP = peer. Если peer — private (loopback **или** RFC1918, напр. Traefik/nginx/Caddy в Docker `172.x`) **и** есть proxy identity headers (`X-Forwarded-For`, `X-Real-IP`, `X-Forwarded-Host`, `X-Forwarded-Proto`, `Forwarded`, `CF-*`, …), запрос **не** считается LAN-клиентом — нужен `devkey`. Прямой LAN/localhost **без** этих заголовков — по-прежнему без ключа. См. `ClientNetworkContext` / `JacRedAccessEvaluator`.

### Политики

| Политика | Правило | Ключи |
| -------- | ------- | ----- |
| **Public** | Всегда разрешено (middleware) | — |
| **ConfigApi** | LAN-клиент **или** valid `devkey` | `X-Dev-Key`, `?devkey=` |
| **DevAdmin** | LAN-клиент **или** valid `devkey` | `X-Dev-Key`, `?devkey=` |
| **ApiKeyWhenConfigured** | Если `apikey` задан — требуется valid key; иначе открыто | `?apikey=`, `X-Api-Key`, `Bearer` |

**Коды отказа:** `OPTIONS` → 204; ключ настроен, но не передан → **401**; иначе → **403**.

> **ConfigApi = DevAdmin** по сети: reverse proxy (same-host loopback **или** Docker/LAN peer с `X-Forwarded-*` / `X-Real-IP`) **сам по себе не заменяет** `devkey`. Нужен прямой LAN-клиент (RFC1918 / loopback **без** proxy identity headers) или заголовок/`?devkey=`.

### Префиксы путей → политика

| Префикс | Политика | Доп. проверка в контроллере |
| ------- | -------- | ---------------------------- |
| `/dev/`, `/cron/`, `/jsondb` | DevAdmin | — |
| `/api/v1.0/config` | ConfigApi | — |
| `/`, `/stats`, `/settings` | Public | Vue SPA (`index.html`) |
| `/health`, `/health/background-jobs`, `/version`, `/lastupdatedb`, `/api/v1.0/conf` | Public | — |
| `/sync/*` | Public | `opensync` для данных sync |
| `/swagger`, `/openapi.yaml`, статика `/assets/` … | Public | `web: true` для UI |
| **Всё остальное** | ApiKeyWhenConfigured | `openstats` для `/stats/*` JSON |

### Доступ по контексту клиента

| Политика | Loopback / LAN без proxy headers | Reverse proxy (loopback или Docker `172.x` + XFF) без devkey | Интернет / удалённый прокси |
| -------- | -------------------------------- | ------------------------------------------------------------ | --------------------------- |
| Public | ✓ | ✓ | ✓ |
| ConfigApi | ✓ | ✗ | `devkey` |
| DevAdmin | ✓ | ✗ | `devkey` (если задан в конфиге) |
| ApiKeyWhenConfigured | `apikey` если задан | `apikey` если задан | `apikey` если задан |

### Белый список без `apikey`

Если в конфиге задан `apikey`, следующие пути **не требуют** его на уровне middleware:

`/`, `/stats`, `/settings`, `/health`, `/health/background-jobs`, `/version`, `/lastupdatedb`, `/openapi.yaml`, `/swagger`, `/api/v1.0/conf`, `/sync/*`

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
| `GET /api/v1.0/trackers` | ApiKeyWhenConfigured | — |
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

Спецификация: OpenAPI **3.0.3**, `info.version` **1.2.0** (источник: [`web/public/openapi.yaml`](web/public/openapi.yaml)). В описании — список `TrackerSlug`, схема `BackgroundJob`, Torznab HEAD и общие query-параметры.

| URL | Назначение |
|-----|------------|
| `GET /swagger` | Swagger UI (интерактивная документация) |
| `GET /swagger/v1/swagger.json` | OpenAPI 3.0 JSON (конвертируется из `web/public/openapi.yaml` → publish `wwwroot/openapi.yaml`) |
| `GET /openapi.yaml` | Статическая OpenAPI 3.0 YAML (source: `web/public/openapi.yaml`) |

Swagger UI по умолчанию загружает **`/openapi.yaml`**; в выпадающем списке также доступен JSON (`/swagger/v1/swagger.json`).

При настроенном `apikey` пути `/swagger`, `/swagger/*` и `/openapi.yaml` доступны без ключа (как `/health`). Схемы авторизации в UI: `apikey` (query), `X-Api-Key`, `Authorization: Bearer`, `X-Dev-Key` (для Config API).

В спецификацию входят публичные эндпоинты (`/api/*`, `/torznab/*`, `/stats/*`, `/sync/*`, `/health`, `/health/background-jobs`, …). Пути `/cron/*`, `/dev/*`, `/jsondb/*` в OpenAPI **не описаны** (политика DevAdmin) — см. Controllers и [`Data/crontab`](Data/crontab).

Типы для веб-UI: `cd web && npm run gen:api` → [`web/src/lib/api/types.ts`](web/src/lib/api/types.ts).

Проверка соответствия маршрутов политикам: [`AccessTraceabilityMatrix.md`](AccessTraceabilityMatrix.md).

### Основные эндпоинты

- **`GET /`** — веб-интерфейс поиска (если `web: true`).
- **`GET /stats`** — страница статистики SPA (если `web: true`; данные — `/stats/torrents`, `/stats/meta`).
- **`GET /settings`** — настройки SPA (Config API: LAN или `X-Dev-Key`).
- **Веб-UI:** Vue 3 SPA в [`web/`](web/) (Vite + Tailwind + shadcn-vue); `make web` / `./scripts/build-web-ui.sh` собирает publish-папку `wwwroot/` (в git не хранится).
- **`GET /health`** — проверка работы. Ответ JSON: `{"status":"OK"}`.
- **`GET /health/background-jobs`** — активные in-process ParseAll / UpdateTasks (cron). Ответ JSON: `{"jobs":[…]}` (пустой массив, если ничего не запущено). Page-only парсеры (`anistar`, `leproduction`, `viruseproject`, `anifilm`) сюда обычно **не** попадают.
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
  - Lampa (`parser_torrent_type=prowlarr`): `query` + `type=tvsearch|search` + `categories` — запрос поднимается до card-поиска как у Jackett (`title`/`title_original`/`year`, `is_serial` 1=фильм / 2=сериал).
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
  - Параметры: `search` / связанные фильтры, `tracker` (один slug или список через запятую — значения `TrackerSlug`), `sort`, `type`, …
- **`GET /api/v1.0/trackers`** — список доступных имён трекеров (`TrackerSlug[]` в OpenAPI): из `synctrackers`, иначе known slugs; записи из `disable_trackers` исключаются. Пустой `synctrackers: []` возвращает `[]` (скан БД не выполняется).
- **`GET /api/v1.0/qualitys`** — список доступных качеств.

### Управление конфигурацией (Config API)

REST API и страница **`/settings`** для редактирования **`init.yaml`** / **`init.conf`**.

**Доступ:** политика **ConfigApi** — LAN-клиент **или** `devkey`. Reverse proxy (loopback или Docker + XFF) без devkey **недостаточен**. При заданном `apikey` — также ключ API для путей вне белого списка.

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

Общие маршруты (не все трекеры реализуют каждый):

- **`GET /cron/{tracker}/parse`** — запуск парсинга (часто с `?page=` / `?limit_page=` / `?fullparse=` — зависит от трекера).
- **`GET /cron/{tracker}/ParseLatest`** — свежие раздачи (Rutor-style: anibelka, korsars, ultradox и ряд старых трекеров).
- **`GET /cron/{tracker}/ParseAllTask`** — фоновый полный обход задач (регистрируется в `/health/background-jobs`).
- **`GET /cron/{tracker}/UpdateTasksParse`** — обновление очереди задач (тоже background-jobs).
- **`GET /cron/{tracker}/parseMagnet`** — парсинг магнет-ссылок (для поддерживающих трекеров).
- Дополнительные параметры: `parseFrom`, `parseTo`, `parseFromDate`, `pages` (зависит от трекера).

Долгие HTTP-джобы для anibelka / korsars / ultradox **не** отменяют работу при обрыве curl (`RequestAborted` не пробрасывается) — дождитесь ответа или смотрите лог `Data/log/{tracker}.log`.

#### Новые трекеры (ориентир из [`Data/crontab`](Data/crontab))

| Трекер | Типичные действия | Расписание в примере crontab |
| ------ | ----------------- | ---------------------------- |
| **anistar** | `parse?limit_page=3` (нужна cookie) | daily `40 6` |
| **leproduction** | `parse?limit_page=3` | daily `45 6` |
| **viruseproject** | `parse?limit_page=3` | daily `50 6` |
| **anifilm** | `parse` (login/cookie; max_time 1800s) | daily `55 6` |
| **anibelka** | `parse`, `UpdateTasksParse`, `ParseAllTask`, `ParseLatest` | hourly + daily tasks |
| **korsars** | то же + login/`bb_data` | hourly + daily tasks |
| **ultradox** | то же (Referer search-like) | hourly + daily tasks |

Полный канон расписания и `max_time` — только в **`Data/crontab`** (через `Data/run-job.sh`).

#### Knaben

- **`GET /cron/knaben/parse`** — свежие раздачи (по умолчанию `from=0`, `size=300`, `pages=1`, `orderBy=date`, `orderDirection=desc`, все TV+Movies категории). Параметры: `from`, `size` (≤300), `pages` (≤10), `query`, `hours`, `orderBy` (`date`|`seeders`|`peers`), `orderDirection` (`desc`|`asc`), `categories` (через запятую). Окно Knaben API: `from + size ≤ 10000`.
- **`GET /cron/knaben/backfill`** — заполнение архива по листовым подкатегориям `2001000`–`2008000` и `3001000`–`3008000`: сначала `asc` (старые), при достижении 10 000 — встречный `desc` (новые). Состояние: **`Data/temp/knaben_backfill.json`**. Параметры: `pages` (≤10), `size` (≤300), `reset=true` — начать заново. Категории ≤10 000 — `complete` за один проход; ≤20 000 — за два; больше 20 000 — `partial` (середина недоступна из‑за лимита API).
- **`GET /cron/knaben/backfillStatus`** — краткий статус checkpoint без запуска.

Пример (как в [`Data/crontab`](Data/crontab)):

```text
12,32,52 * * * *  /opt/jacred/Data/run-job.sh knaben-parse http://127.0.0.1:9117/cron/knaben/parse 900
42 * * * *  /opt/jacred/Data/run-job.sh knaben-backfill "http://127.0.0.1:9117/cron/knaben/backfill?pages=10" 900
```

Ручной старт архива с `asc`:

```text
curl -s "http://127.0.0.1:9117/cron/knaben/parse?from=0&size=300&pages=10&orderBy=date&orderDirection=asc&categories=2001000"
```

### Обслуживание FDB (`/cron/maintenance` и CLI `maintain`)

Единый проход по FileDB (ключи бакетов + shard-файлы) на битые/устаревшие/несогласованные данные.

**Online (HTTP):** фоновый job (как `ParseAllTask`): `Check` сразу возвращает `ok` / `work`, результат — в `Status` и `Data/temp/maintenance-last.json`. Лимит wall-clock онлайн-джоба — 6 часов.

| Эндпоинт | Описание |
| --------- | --------- |
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
| ---- | --------- |
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

**Доступ (HTTP):** политика **DevAdmin** (`/cron/*`). Подробные таблицы LAN / tunnel / ключи — в разделе **[Безопасность и доступ к API](#безопасность-и-доступ-к-api)**.

HTTP-вызовы `/cron/*` логируются с префиксом `cron:` (уровень зависит от `logging.cronSkipFastMs`).

**Пример `curl` при включённых `apikey` и `devkey`:**

```bash
curl -s -H "X-Api-Key: YOUR_API_KEY" -H "X-Dev-Key: YOUR_DEV_KEY" \
  "http://127.0.0.1:9117/cron/rutor/parse"
```

---

## Сборка

Предпочтительный интерфейс — **`make`** (см. `make help`). Скрипты сборки лежат в [`scripts/`](scripts/).

### Требования для сборки

- **.NET 10.0 SDK** (см. **`JacRed.csproj`**)
- **Node.js 22+** (сборка Vue SPA в `wwwroot/`)
- **Git** (для генерации версии из тегов)
- **Bash** (для скриптов сборки)
- **Make** (GNU Make / BSD Make)

### Сборка для текущей платформы

```bash
make publish
```

### Сборка для конкретной платформы (RID)

```bash
make publish RID=linux-arm64
make publish RID="linux-x64 osx-arm64"
make publish-linux-arm64
```

### Сборка для всех платформ

```bash
make publish-all
```

### Другие цели

```bash
make web       # только SPA → wwwroot/
make test      # .NET тесты
make docker    # docker build -t jacred .
make clean
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
- Версия генерируется автоматически из Git тегов через `scripts/generate-version.sh`

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

Готовый пример: **`docker-compose.example.yml`** (JacRed + FlareSolverr, named volumes).

**Полезно:**

- **Конфиг:** после первого запуска настройте **`init.yaml`** или **`init.conf`** в томе `jacred-config` или каталоге `./config` (при bind mount). Конфиг автоматически копируется из `/app/config/` в `/app/` при старте контейнера. Для Rutracker в compose задайте `flaresolverr.url: http://flaresolverr:8191/v1`.
- **Порты:** веб-интерфейс и API доступны на порту **9117** (при необходимости измените маппинг `ports` и `listenport` в конфиге). Порт FlareSolverr **8191** в примере не публикуется наружу — только внутренняя сеть Compose.
- **Память:** JacRed + FlareSolverr (~1 GiB) — ориентир **≥4 GiB** на сервис JacRed в примере compose; при большой базе увеличьте лимит.
- **Тома:**
  - `jacred-config` — хранит конфигурацию (`init.yaml` или `init.conf`)
  - `jacred-data` — хранит базу данных (`fdb/`), логи (`log/`), временные файлы (`temp/`) и треки (`tracks/`)
- **Healthcheck:** контейнер включает встроенный healthcheck, проверяющий доступность `/health` эндпоинта.
- **Сборка своего образа:** в корне репозитория выполните `docker build -t jacred .` и в примерах выше замените образ на `jacred:latest`.
- **Переменные окружения:** поддерживаются `TZ` (часовой пояс) и `UMASK` (права на файлы, по умолчанию `0027`).

### Самостоятельный парсинг и расписание (cron) в Docker

В образе **нет** планировщика **cron** (и **нет** установки заданий в crontab внутри контейнера). Фоновые циклы приложения (синхронизация по `syncapi`, статистика и т.д.) работают сами; **периодический вызов HTTP-эндпоинтов** `/cron/...` и **`/jsondb/save`** нужно организовать **снаружи** контейнера.

**Типовые варианты:**

1. **Cron на хосте** (чаще всего) — установить crontab из **`Data/crontab`** (вызовы [`Data/run-job.sh`](Data/run-job.sh)) или вручную дергать `curl` на опубликованный порт (например `http://127.0.0.1:9117/...`). Запрос с хоста в контейнер обычно приходит с адреса из **приватной подсети** (в т.ч. шлюз Docker `172.x`), что удовлетворяет проверке «локальная/приватная сеть» в приложении.
2. **Отдельный контейнер с cron** — маленький образ (например `curl` + `cron`), в том же Docker Compose, который по расписанию дергает сервис JacRed по **внутреннему** имени и порту (например `http://jacred:9117/...`). Убедитесь, что с точки зрения JacRed IP источника остаётся в приватном диапазоне (типично так и есть в user-defined bridge-сети).
3. **Kubernetes CronJob**, **systemd timer** на хосте — по сути то же, что п.1: периодический HTTP-запрос к JacRed.

**Ориентир по расписанию:** в репозитории лежит пример **`Data/crontab`** (парсинг по трекерам через `Data/run-job.sh`, `cloudflare-warmup` перед `rutracker-parse`, daily anistar/leproduction/viruseproject/anifilm, hourly anibelka/korsars/ultradox + ParseLatest, knaben parse/backfill, и `*/5 * * * *` для **`/jsondb/save`**). Скопируйте нужные строки в свой crontab на хосте (или в свой шаблон для контейнера с cron) и:

- при использовании `run-job.sh` убедитесь, что скрипт доступен по пути из crontab (в релизе — `/opt/jacred/Data/run-job.sh`); либо замените строки на прямой `curl`;
- замените хост/порт в URL на ваши (`127.0.0.1:9117` или имя сервиса в Compose);
- если в **`init.yaml` / `init.conf`** задан **`apikey`** — добавьте в каждый URL `?apikey=...` (или в `curl` `-H "X-Api-Key: ..."`), иначе запросы к `/cron/*` и `/jsondb/save` получат **401**;
- если задан **`devkey`** и запрос считается «локальным» — добавьте `?devkey=...` или `-H "X-Dev-Key: ..."`.

Подробнее про ключи для `/cron/*` — в разделе **«Парсинг трекеров»** выше.

**Синхронизация вместо своего парсинга:** можно указать **`syncapi`** и не вызывать `/cron/*` вовсе; тогда достаточно конфигурации и встроенных циклов приложения (плюс при необходимости **`/jsondb/save`** по расписанию, если вы ведёте локальную запись БД).

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
- **Конфиг не подхватывается:** рабочий файл — `./init.yaml` (CWD рядом с бинарником); правка только `Data/init.yaml` без копии/symlink в корень не применяется
- **Korsars:** в логе `login.u empty` / `login failed` — задайте `Korsars.login` или `cookie` с `bb_data` в корневом `init.yaml`
- **Anistar:** пустой parse при 403/CF — нужна cookie; встроенный Rutracker Warmup не помогает
- **Anibelka:** не логиньтесь — анонимный download
- **Rutracker / Cloudflare:** проверьте, что FlareSolverr доступен (`curl http://127.0.0.1:8191/` или `http://flaresolverr:8191/` в compose), в конфиге `flaresolverr.enable: true` и верный `url`, и что срабатывает warmup: `curl http://127.0.0.1:9117/cron/cloudflare/Warmup` (первый ответ может занять до ~180 с). Если на VPS challenge детектится, но не решается — задайте residential/ISP `PROXY_*` у контейнера FlareSolverr (см. playbook в Rutracker README). Smoke: `./scripts/cron_rutracker_smoke.sh`

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
- FlareSolverr держит ~600–700 МБ на сессию Chromium; при простое сессия закрывается через `flaresolverr.sessionIdleMinutes` (по умолчанию 30)

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
