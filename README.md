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
- 🌐 **Веб-интерфейс** для просмотра и управления
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

- **.NET 9.0** (для запуска из исходников)
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

- Настройте конфиг: **`/opt/jacred/init.yaml`** или **`/opt/jacred/init.conf`**
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
| `apikey` | Ключ авторизации API (пусто — без проверки). Передаётся через `?apikey=...`, заголовок `X-Api-Key` или `Authorization: Bearer`. Без ключа доступны: `/`, `/stats`, `/health`, `/version`, `/lastupdatedb`, `/api/v1.0/conf`, `/sync/*` | — |
| `devkey` | Ключ для доступа к `/dev/`, `/cron/`, `/jsondb` за туннелем/прокси (пусто — только локальная сеть). Передаётся через заголовок `X-Dev-Key` или параметр `?devkey=...` | — |
| `mergeduplicates` | Объединять дубликаты в выдаче | `true` |
| `mergenumduplicates` | Объединять дубликаты по номеру (серии и т.п.) | `true` |
| `openstats` | Открыть доступ к `/stats/*` | `true` |
| `opensync` | Разрешить синхронизацию базы через sync API | `false` |
| `opensync_v1` | Разрешить старый формат sync v1 | `false` |
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
| `syncapi` | URL сервера с открытым `opensync` для загрузки базы (пусто — не использовать) | `""` |
| `synctrackers` | Список трекеров для синхронизации с syncapi | см. example |
| `disable_trackers` | Трекеры, не участвующие в синке (RIP и др.) | `hdrezka`, `anifilm`, `anilibria` |
| `timeSync` | Интервал синхронизации с syncapi, мин | `120` |
| `timeSyncSpidr` | Интервал синхронизации Spidr, мин | `360` |
| `syncsport` | Включить синхронизацию по спорту | `false` |
| `syncspidr` | Включить синхронизацию Spidr | `false` |

### Логирование

| Параметр | Описание | По умолчанию |
| ---------- | ---------- | -------------- |
| `log` | Устаревший: включить логи fdb и парсеров | `false` |
| `logFdb` | Писать лог добавлений/обновлений в Data/log/fdb.*.log | `false` |
| `logFdbRetentionDays` | Хранить логи fdb не более N дней (0 — без ограничения) | `7` |
| `logFdbMaxSizeMb` | Макс. суммарный размер логов fdb, МБ (0 — без ограничения) | `0` |
| `logFdbMaxFiles` | Макс. число файлов логов fdb (0 — без ограничения) | `0` |
| `logParsers` | Включить логи парсеров по трекерам (Data/log/{tracker}.log) | `false` |

### Статистика и треки

| Параметр | Описание | По умолчанию |
| ---------- | ---------- | -------------- |
| `timeStatsUpdate` | Интервал обновления статистики, мин | `90` |
| `tracks` | Включить сбор метаданных треков (tsuri) | `false` |
| `trackslog` | Включить логи модуля tracks (Data/log/tracks.log) | `false` |
| `trackscategory` | Категория для торрентов из jacred (рекомендуется задавать уникально для каждого инстанса) | `jacred` |
| `tracksatempt` | Количество неудачных попыток извлечь дорожки, после этого торрент исключается из tracks | `20` |
| `tracksmod` | Режим треков: 0 — все, 1 — только за текущие сутки | `0` |
| `tracksdelay` | Задержка между запросами к tsuri, мс | `20000` |
| `tracksinterval` | Интервалы запуска задач tracks (task1 — за последние сутки, task0 — остальные), мин | `task1: 60, task0: 180` |
| `tsuri` | URL сервиса анализа треков (массив) | `["http://127.0.0.1:8090"]` |

### Трекеры (блоки в конфиге)

Для каждого трекера можно задать следующие параметры:

| Параметр | Описание | Пример |
| ---------- | ---------- | -------- |
| `host` | Основной URL трекера | `https://rutracker.org` |
| `alias` | Альтернативный URL (например, .onion адрес) | `http://rutracker....onion` |
| `useproxy` | Использовать прокси для этого трекера | `true` / `false` |
| `reqMinute` | Максимальное число запросов в минуту | `8` |
| `parseDelay` | Задержка между запросами при парсинге, мс | `7000` |
| `log` | Включить логи парсера для этого трекера | `true` / `false` |
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

Для самостоятельного парсинга трекеров:

1. Настроить **`init.yaml`** или **`init.conf`** (примеры в **`Data/example.yaml`**, **`Data/example.conf`**).
   - Убедитесь, что для нужных трекеров указаны правильные `host`, `login` (если требуется) или `cookie`.
   - Настройте прокси, если требуется доступ к .onion доменам.

2. Выберите режим работы:
   - **Парсинг через cron:** По умолчанию база скачивается при установке, парсинг выполняется по расписанию из **`Data/crontab`**. Активируйте: `crontab /opt/jacred/Data/crontab`
   - **Синхронизация:** Укажите **`syncapi`** в конфиге, чтобы подтягивать базу с удалённого сервера. Включите `opensync: true` для участия в синхронизации.

3. **Важно:** В crontab по умолчанию используется порт **9117** — при смене порта измените URL в crontab или используйте переменную окружения.

4. Мониторинг парсинга:
   - Логи парсеров: `Data/log/{tracker}.log` (если `logParsers: true` или `log: true` для конкретного трекера)
   - Логи БД: `Data/log/fdb.*.log` (если `logFdb: true`)
   - Статистика: `GET /stats/*` (если `openstats: true`)

---

## Доступ к доменам .onion

1. Запустить Tor на порту 9050.
2. В конфиге задать для трекера **`alias`** с .onion-адресом и в **`globalproxy`** правило с `pattern: "\\.onion"` и `list: ["socks5://127.0.0.1:9050"]` (как в примере выше).

---

## API

### Основные эндпоинты

- **`GET /`** — веб-интерфейс (если `web: true`).
- **`GET /health`** — проверка работы. Ответ JSON: `{"status":"OK"}`.
- **`GET /version`** — версия приложения. Ответ JSON: `{"version":"1.0.0"}`.
- **`GET /lastupdatedb`** — дата/время последнего обновления БД (UTC). Ответ JSON: `{"lastupdatedb":"dd.MM.yyyy HH:mm"}`.

### API поиска

- **`GET /api/v2.0/indexers/{status}/results`** — поиск в формате Jackett (совместимость с Jackett API).
  - Параметры: `Query` (поисковый запрос), `Category` (категория), `Tracker` (трекер), `apikey` (если настроен).
- **`GET /api/v1.0/torrents`** — поиск торрентов (собственный API).
  - Параметры: `query` (поисковый запрос), `tracker` (трекер), `category` (категория), `quality` (качество).
- **`GET /api/v1.0/qualitys`** — список доступных качеств.

### Управление

- **`GET /api/v1.0/conf`** — проверка apikey (`?apikey=...`).
- **`GET /jsondb/save`** — сохранить БД на диск (при использовании syncapi скрипт установки не вызывает save; при собственном парсинге cron вызывает save по расписанию).
  - Доступ: только из локальной сети или с `devkey` (заголовок `X-Dev-Key` или параметр `?devkey=...`).

### Разработка и отладка

- **`GET /dev/*`** — инструменты разработки и отладки БД (только localhost или с `devkey`).
  - Доступ: только из локальной сети или с `devkey` (заголовок `X-Dev-Key` или параметр `?devkey=...`).

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

### Статистика и синхронизация

- **`GET /stats/*`** — статистика (если `openstats: true`).
- **`GET /sync/*`** — эндпоинты синхронизации (если `opensync: true`).
  - Поддерживаются форматы v1 и v2 (v1 требует `opensync_v1: true`).

### Парсинг трекеров

- **`GET /cron/{tracker}/parse`** — запуск парсинга трекера.
- **`GET /cron/{tracker}/ParseAllTask`** — парсинг всех задач трекера.
- **`GET /cron/{tracker}/UpdateTasksParse`** — обновление задач парсинга.
- **`GET /cron/{tracker}/parseMagnet`** — парсинг магнет-ссылок (для поддерживающих трекеров).
- Дополнительные параметры: `parseFrom`, `parseTo`, `parseFromDate` (зависит от трекера).
- Доступ: только из локальной сети или с `devkey` (заголовок `X-Dev-Key` или параметр `?devkey=...`).

**Примечания:**

> - Для использования API с авторизацией укажите `apikey` в конфиге. Ключ можно передать: `?apikey=...`, заголовок `X-Api-Key` или `Authorization: Bearer`. Пути `/`, `/stats`, `/health`, `/version`, `/lastupdatedb`, `/api/v1.0/conf`, `/sync/*` доступны без ключа.
> - Эндпоинты `/cron/*`, `/dev/*`, `/jsondb/*` доступны только из локальной/приватной сети (по IP клиента). При работе за туннелем/прокси (когда все запросы выглядят локальными) используйте `devkey` для защиты этих эндпоинтов.

---

## Сборка

### Требования для сборки

- **.NET 9.0 SDK** (см. **`JacRed.csproj`**)
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

Результат сборки находится в каталоге **`dist/<platform>/`** (single-file, self-contained).

### Особенности сборки

- Single-file публикация (один исполняемый файл)
- Self-contained (включает .NET runtime)
- Оптимизация для скорости выполнения
- Сжатие в single-file
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

- Проверьте, что cron настроен правильно: `crontab -l`
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

JacRed построен на **ASP.NET Core** (.NET 9.0) и использует:

- **Файловая БД (fdb)** — структурированное хранение данных в файловой системе
- **Асинхронные задачи** — фоновые процессы для парсинга, синхронизации и статистики
- **Автоматическая перезагрузка конфига** — изменения применяются без перезапуска (каждые 10 секунд)
- **Модульная архитектура** — отдельные контроллеры для каждого трекера

### Основные компоненты

- **ModHeaders** — middleware: ограничение по IP (локальная/приватная сеть для `/cron/`, `/dev/`, `/jsondb`), проверка `devkey` и `apikey`, CORS `Access-Control-Allow-Private-Network`, логирование cron
- **FileDB** — управление файловой базой данных
- **SyncCron** — синхронизация с удалёнными серверами
- **TrackersCron** — планирование и выполнение парсинга трекеров
- **StatsCron** — сбор и обновление статистики
- **TracksCron** — сбор метаданных треков (опционально)
- **ApiController** — обработка API запросов
- **SyncController** — эндпоинты синхронизации

---

## Лицензия

MIT License. См. файл [LICENSE](LICENSE) для подробностей.
