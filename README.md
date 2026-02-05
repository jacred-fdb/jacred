<div align="center">
  <img src="wwwroot/img/jacred-social-preview.png" alt="Jacred-FDB — Torrent aggregator & file database" width="480">
</div>

# <img src="wwwroot/img/jacred.png" width="32" height="32" alt=""> JacRed

[![Build](https://github.com/jacred-fdb/jacred/actions/workflows/build.yml/badge.svg)](https://github.com/jacred-fdb/jacred/actions/workflows/build.yml)

Агрегатор торрент-трекеров с API в формате Jackett. Хранит данные в файловой БД (fdb), поддерживает синхронизацию с удалённой базой и самостоятельный парсинг трекеров по cron.

## AI Документация

[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/jacred-fdb/jacred)

---

## Требования

- **.NET 9.0** (для запуска из исходников)
- Для установки скриптом: **Linux** (systemd, cron), рекомендуется Debian/Ubuntu

---

## Установка

Установка одной командой (запускать от любого пользователя, при необходимости запросится sudo):

```bash
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | bash
```

Скрипт устанавливает приложение в **`/opt/jacred`**, создаёт пользователя и systemd-сервис `jacred`, добавляет cron для сохранения БД и при первом запуске по желанию скачивает готовую базу.

**Опции:**

| Опция | Описание |
|-------|----------|
| `--no-download-db` | Не скачивать и не распаковывать базу (только при установке) |
| `--update` | Обновить приложение с последнего релиза (сохранить БД, заменить файлы, перезапустить) |
| `--remove` | Полностью удалить JacRed (сервис, cron, каталог приложения) |
| `-h`, `--help` | Показать справку |

**Примеры:**

```bash
# Обычная установка
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | bash

# Установка без загрузки базы
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | bash -s -- --no-download-db

# Обновление уже установленного приложения
sudo /opt/jacred/jacred.sh --update

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

- Настройте конфиг: **`/opt/jacred/init.yaml`** или **`/opt/jacred/init.conf`**
- Перезапуск: `systemctl restart jacred`
- Полный crontab для парсинга: `crontab /opt/jacred/Data/crontab`

> **Важно:** по умолчанию включена синхронизация базы с внешнего сервера (`syncapi`). Для самостоятельного парсинга настройте cron по `Data/crontab` и при необходимости отключите или измените `syncapi`.

---

## Конфигурация

Приоритет файлов: **`init.yaml`** > **`init.conf`**. Если существуют оба, используется `init.yaml`. Конфиг перечитывается автоматически каждые 10 секунд.

Примеры полного конфига: **`Data/example.yaml`**, **`Data/example.conf`**. В рабочем конфиге указывайте только те параметры, которые нужно изменить.

### Основные параметры

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `listenip` | IP для прослушивания (`any` — все интерфейсы) | `any` |
| `listenport` | Порт HTTP | `9117` |
| `apikey` | Ключ авторизации API (пусто — без проверки) | — |
| `mergeduplicates` | Объединять дубликаты в выдаче | `true` |
| `mergenumduplicates` | Объединять дубликаты по номеру (серии и т.п.) | `true` |
| `openstats` | Открыть доступ к `/stats/*` | `true` |
| `opensync` | Разрешить синхронизацию базы через sync API | `true` |
| `opensync_v1` | Разрешить старый формат sync v1 | `false` |
| `web` | Раздавать статику (веб-интерфейс) | `true` |
| `maxreadfile` | Макс. число открытых файлов за один поисковый запрос | `200` |
| `evercache` | Кеш открытых файлов (рекомендуется при высокой нагрузке) | см. example |
| `fdbPathLevels` | Уровни вложенности каталогов fdb | `2` |

### Синхронизация

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `syncapi` | URL сервера с открытым `opensync` для загрузки базы | задаётся в example |
| `synctrackers` | Список трекеров для синхронизации с syncapi | см. example |
| `disable_trackers` | Трекеры, не участвующие в синке (RIP и др.) | `hdrezka`, `anifilm`, `anilibria` |
| `timeSync` | Интервал синхронизации с syncapi, мин | `60` |
| `timeSyncSpidr` | Интервал синхронизации Spidr, мин | `60` |
| `syncsport` | Включить синхронизацию по спорту | `true` |
| `syncspidr` | Включить синхронизацию Spidr | `true` |

### Логирование

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `log` | Устаревший: включить логи fdb и парсеров | `false` |
| `logFdb` | Писать лог добавлений/обновлений в Data/log/fdb.*.log | `false` |
| `logFdbRetentionDays` | Хранить логи fdb не более N дней (0 — без ограничения) | `7` |
| `logFdbMaxSizeMb` | Макс. суммарный размер логов fdb, МБ (0 — без ограничения) | `0` |
| `logFdbMaxFiles` | Макс. число файлов логов fdb (0 — без ограничения) | `0` |
| `logParsers` | Включить логи парсеров по трекерам (Data/log/{tracker}.log) | `false` |

### Статистика и треки

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `timeStatsUpdate` | Интервал обновления статистики, мин | `90` |
| `tracks` | Включить сбор метаданных треков (tsuri) | `false` |
| `tracksmod` | Режим треков: 0 — все, 1 — день/месяц | `0` |
| `tracksdelay` | Задержка между запросами к tsuri, мс | `20000` |
| `tsuri` | URL сервиса анализа треков | `["http://127.0.0.1:8090"]` |

### Трекеры (блоки в конфиге)

Для каждого трекера можно задать: `host`, `alias` (например .onion), `useproxy`, `reqMinute`, `parseDelay`, `log`, при необходимости `login` (u, p) или `cookie`. Полный список и значения по умолчанию — в **`Data/example.yaml`** / **`Data/example.conf`**.

### Прокси

- **`proxy`** — общие настройки прокси (pattern, list, useAuth, username, password, BypassOnLocal).
- **`globalproxy`** — массив правил (например, для доменов `.onion` через Tor: `pattern: "\\.onion"`, `list: ["socks5://127.0.0.1:9050"]`).

### Пример минимального конфига (YAML)

```yaml
listenport: 9120
syncapi: https://jacred.example.com

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
    { "pattern": "\\.onion", "list": ["socks5://127.0.0.1:9050"] }
  ]
}
```

---

## Источники (трекеры)

**Активные (парсинг и/или синхронизация):**  
Kinozal, NNMClub, Rutor, TorrentBy, Bitru (в т.ч. Bitru API), Rutracker, Megapeer, Selezen, Toloka, Mazepa, Baibako, Lostfilm, Animelayer.

**RIP (отключены по умолчанию, только синхронизация со старых баз):**  
Anifilm, AniLibria, HDRezka.

Список для `synctrackers` и настройки по трекерам см. в **`Data/example.yaml`**.

---

## Самостоятельный парсинг

1. Настроить **`init.yaml`** или **`init.conf`** (примеры в **`Data/example.yaml`**, **`Data/example.conf`**).
2. Либо указать **`syncapi`** в конфиге (тогда база подтягивается с сервера), либо настроить cron по файлу **`Data/crontab`** (вызовы `/cron/{tracker}/parse` и др.). В crontab по умолчанию используется порт **9117** — при смене порта измените URL в crontab.

---

## Доступ к доменам .onion

1. Запустить Tor на порту 9050.
2. В конфиге задать для трекера **`alias`** с .onion-адресом и в **`globalproxy`** правило с `pattern: "\\.onion"` и `list: ["socks5://127.0.0.1:9050"]` (как в примере выше).

---

## API

- **`GET /`** — веб-интерфейс (если `web: true`).
- **`GET /health`** — проверка работы. Ответ JSON: `{"status":"OK"}`.
- **`GET /version`** — версия приложения. Ответ JSON: `{"version":"1.0.0"}`.
- **`GET /lastupdatedb`** — дата/время последнего обновления БД (UTC). Ответ JSON: `{"lastupdatedb":"dd.MM.yyyy HH:mm"}`.
- **`GET /api/v1.0/conf`** — проверка apikey (`?apikey=...`).
- **`GET /api/v2.0/indexers/{status}/results`** — поиск в формате Jackett (query, title, category и т.д.).
- **`GET /api/v1.0/torrents`** — поиск торрентов (собственный API).
- **`GET /api/v1.0/qualitys`** — список качеств.
- **`GET /jsondb/save`** — сохранить БД на диск (при использовании syncapi скрипт установки не вызывает save; при собственном парсинге cron вызывает save по расписанию).
- **`GET /stats/*`** — статистика (если `openstats: true`).
- **`/sync/*`** — эндпоинты синхронизации (если `opensync: true`).
- **`/cron/{tracker}/*`** — запуск парсинга по трекерам (parse, ParseAllTask, UpdateTasksParse, parseMagnet и др. в зависимости от трекера).

---

## Сборка

- **.NET 9.0**, см. **`JacRed.csproj`**.

Сборка под Linux (amd64/arm64), Windows (x64), macOS (arm64/amd64):

```bash
./build.sh
```

Результат в каталоге **`dist/<platform>/`** (single-file, self-contained).

---

## Docker

<https://github.com/pavelpikta/docker-jacred-fdb>

---

## Роутер (Cloudflare Worker)

В каталоге **`router/`** находится Cloudflare Worker для маршрутизации запросов по хосту/пути на разные бэкенды (домашний сервер, Tailscale, туннели, Pages, Vercel) с кешированием и заголовками. Документация и настройка — в **`router/README.md`**.

---

## Лицензия и репозиторий

Исходный код: [github.com/jacred-fdb/jacred](https://github.com/jacred-fdb/jacred).
