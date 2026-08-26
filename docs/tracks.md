---
title: Tracks
description: Модуль tracks — TorrServer, ffprobe, concurrency
tags:
  - ops
  - tracks
---
# Статистика и треки (модуль tracks)

| Параметр | Описание | По умолчанию |
| --- | --- | --- |
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
| --- | --- |
| `stats.json` | Сводка по трекерам для UI `/stats` |
| `stats-meta.json` | `{ updatedAt, trackerCount }` — время последнего сбора |
| `tracks-stats.json` | Кэш export-статистики ffprobe/tracks (`/stats/tracks`, `/dev/TracksStats`) |
| `tracks-index.bz` | Gzip-индекс infohash в `Data/tracks` (быстрый старт и stats без walk всех JSON) |

**Эндпоинты (UI `/stats`):** `GET /stats/torrents` — сводка из `stats.json`; `GET /stats/tracks` — агрегат из `tracks-stats.json`; `GET /stats/meta` — `updatedAt`. Force refresh: `/dev/TracksStats?refresh=true`.

**Старт сервиса:** HTTP (`/health`) доступен через ~10–30 с после загрузки `masterDb.bz`. Индекс треков `Data/temp/tracks-index.bz` и первый сбор stats выполняются **в фоне**; пока индекс пуст, cron stats **откладывается** (в логе: `stats: deferred`). После rebuild индекса stats запускается автоматически.

**Счётчики tracks (confirm/wait/skip)** в `stats.json`: `confirm` — трек есть в tracks DB (RAM / индекс / файл с непустым `streams`, как `HasTrackForTorrent`); `wait` — magnet есть, трека нет; `skip` — `ffprobe_tryingdata ≥ tracksatempt`. Поле `ffprobe` в FileDB не канонично.

Результаты анализа сохраняются в **`Data/tracks/{aa}/{b}/{hash}.json`**. Экспорт, backfill и статистика — эндпоинты **`/dev/TracksStats`**, **`/dev/ExportTracks`**, **`/dev/BackfillTracks`** (см. [Разработка и отладка](api.md#dev-debug)).

## Параллелизм tracks (`tracksconcurrency`) и TorrServer

Модуль tracks запускает **5 фоновых очередей (typetask)** по возрасту/типу торрентов плюс **отдельный orphan sweep**:

| typetask | Окно | Кого берёт |
| --- | --- | --- |
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
| --- | --- |
| `нет сидов/данных — пропуск /ffp` | Мёртвый торрент, early abort |
| `нет подходящего media-файла` | Все file id вернули 400 |
| `ffp timeout` (504) | `/ffp` не успел за выбранный ffp-таймаут |
| `overall …s` (408) | Общий лимит analyze (см. формулу выше) |
| `Backoff …ms` | Пауза после down/timeout TS или API-ошибки |
| `orphan sweep: rem` | Периодическая очистка «сирот» в `trackscategory` |
| `уже анализируется — пропуск` | Per-hash lock (дубль в очереди / между typetask) |

Выбор TorrServer: из массива **`tsuri`** берётся сервер с **наименьшей** загрузкой в категории **`trackscategory`**.

**Матрица: число TorrServer × `tracksconcurrency`**

| Серверы | `tracksconcurrency = 2` | `= 3` | `= 5` |
| --- | --- | --- | --- |
| **1 TorrServer** | до 2 `/ffp` одновременно; мягко для слабого TS | до 3; баланс скорости и нагрузки | до 5; быстро, но тяжело для одного TS (ffprobe + play) |
| **2 TorrServer** | 2 слота на весь пул; часто ~1 job на TS | ~2+1 между TS | до ~2–3 на TS при полной очереди |
| **3 TorrServer** | максимум 2 TS заняты одновременно | **оптимально: по 1 job на TS** | до ~2+2+1; быстрый разбор backlog |

**Пример (3 TS, `tracksconcurrency: 3`):** typetask 1, 2 и 5 одновременно взяли кандидатов → три слота на TS-A, TS-B, TS-C; typetask 3 ждёт, пока один слот освободится после rem.

**Грубая оценка нагрузки на один TS при полной очереди:**

`нагрузка ≈ min(tracksconcurrency, активные_typetask, кол-во_tsuri) / кол-во_tsuri`

**Рекомендуемые значения:**

| Схема | `tracksconcurrency` |
| --- | --- |
| 1 TS (VPS / слабый) | **2** |
| 1 TS (выделенный, мощный) | **3** |
| 2 TS | **3** |
| 3 TS, обычный backlog | **3** (по одному job на TS) |
| 3 TS, большой backlog, TS не перегружены | **5** |

Каноническое хранение ffprobe — **`Data/tracks`**, не поле `ffprobe` в FileDB. Cron пропускает торренты, для которых трек уже есть в tracks DB (индекс / RAM / файл).
