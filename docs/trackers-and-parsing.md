---
title: Трекеры и парсинг
description: Список трекеров, самостоятельный парсинг, .onion
tags:
  - ops
  - trackers
---
# Трекеры и парсинг

## Источники (трекеры)

Известные slug’и (`ConfigSchema.KnownTrackerSlugs` / OpenAPI `TrackerSlug`, 25 шт.):

`anibelka`, `anidub`, `anifilm`, `aniliberty`, `animelayer`, `anistar`, `baibako`, `bitru`, `kinozal`, `knaben`, `korsars`, `leproduction`, `lostfilm`, `mazepa`, `megapeer`, `nnmclub`, `rudub`, `rutor`, `rutracker`, `selezen`, `subsplease`, `toloka`, `torrentby`, `ultradox`, `viruseproject`.

**Парсеры (cron + FileDB):** все slug’и выше имеют контроллер `/cron/{slug}/…` (кроме служебных `cloudflare` / `maintenance`).

**Иконки UI:** `/img/ico/{slug}.ico` (fallback `/img/ico/default.ico`). Файлы вроде `anilibria.ico` / `hdrezka.ico` / `underverse.ico` — устаревшие ассеты, **не** активные трекеры.

**Не добавляйте в `synctrackers`:** retired-трекеры (AniLibria, HDRezka и т.п.). При фильтрации остатков sync — `disable_trackers`.

Список для `synctrackers` и блоки настроек — в [`Data/example.yaml`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.yaml). Конфиг приложения при запуске: **`init.yaml`** / **`init.conf`** в **корне рабочего каталога** (рядом с бинарником), не `Data/init.yaml` (тот — шаблон/defaults при установке).

---

## Самостоятельный парсинг

Для самостоятельного парсинга трекеров:

1. Настроить **`init.yaml`** или **`init.conf`** (примеры в [`Data/example.yaml`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.yaml), [`Data/example.conf`](https://github.com/jacred-fdb/jacred/blob/main/Data/example.conf)).
   - Убедитесь, что для нужных трекеров указаны правильные `host`, `login` / `cookie` (см. [таблицу аутентификации](configuration.md#trackers-config)).
   - Добавьте slug’и в **`synctrackers`**, если хотите видеть их в `GET /api/v1.0/trackers` и sync-фильтре.
   - Настройте прокси, если требуется доступ к .onion доменам.
   - **Rutracker / Cloudflare:** блок **`flaresolverr`** + на VPS egress через **WARP SOCKS** (`PROXY_URL` у контейнера FlareSolverr, volume для `/var/lib/cloudflare-warp`). Cookie `cf_clearance` живёт в persistent-сессии FlareSolverr — держите `sessionIdleMinutes` и keep-alive Warmup. `network_mode: host` сам IP не меняет. Альтернатива без FlareSolverr — Worker **`Rutracker.alias`**. Подробности: [`Infrastructure/Trackers/Rutracker/README.md`](https://github.com/jacred-fdb/jacred/blob/main/Infrastructure/Trackers/Rutracker/README.md).
   - **Anistar:** `host: https://anistar.org` (FDB urls), `alias: https://v30.astar.bz` (fetch). При смене зеркала меняйте только `alias`. FlareSolverr-warmup Rutracker не используется.

2. Выберите режим работы:
   - **Парсинг через cron:** По умолчанию база скачивается при установке, парсинг выполняется по расписанию из [`Data/crontab`](https://github.com/jacred-fdb/jacred/blob/main/Data/crontab) (включая `cloudflare-warmup` за ~5 мин до `rutracker-parse`, daily page-парсеры и hourly Rutor-style для anibelka/korsars/ultradox/rudub/subsplease). Активируйте: `crontab /opt/jacred/Data/crontab`
   - **Синхронизация:** Укажите **`syncapi`** в конфиге, чтобы подтягивать базу с удалённого сервера. Включите `opensync: true` для участия в синхронизации.
   - **Docker:** в образе нет cron — расписание выносится на хост, отдельный контейнер или оркестратор; см. [Docker → cron](docker.md#cron-docker).

3. **Важно:** В crontab по умолчанию используется порт **9117** — при смене порта измените URL в строках [`Data/crontab`](https://github.com/jacred-fdb/jacred/blob/main/Data/crontab). Если в конфиге задан **`apikey`** / **`devkey`**, добавьте их в URL (`?apikey=...` / `?devkey=...`) в каждой строке crontab (см. [Безопасность](security.md)). Задания вызывают [`Data/run-job.sh`](https://github.com/jacred-fdb/jacred/blob/main/Data/run-job.sh) (`flock` + `curl --max-time`).

4. Мониторинг парсинга:
   - Логи парсеров: `Data/log/{tracker}.log` (по умолчанию `logParsers: true`, per-tracker `log: true`)
   - Логи БД: `Data/log/fdb.*.log` (по умолчанию `logFdb: true`)
   - Активные длинные джобы: `GET /health/background-jobs` (ParseAll / UpdateTasks; page-only парсеры туда обычно не попадают)
   - Полный обход ParseAllTask: checkpoint цикла в `Data/temp/{tracker}_parseAllCycle.json` (не сбрасывается в полночь; см. [API → ParseAllTask](api.md#parsing-trackers))
   - Статистика: `GET /stats/*` (если `openstats: true`)

---

## Доступ к доменам .onion

1. Запустить Tor на порту 9050.
2. В конфиге задать для трекера **`alias`** с .onion-адресом и в **`globalproxy`** правило с `pattern: "\\.onion"` и `list: ["socks5://127.0.0.1:9050"]` (как в [примере прокси](configuration.md#globalproxy)).
