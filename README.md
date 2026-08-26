# JacRed

![Jacred — A Torrent aggregator & file database](web/public/img/jacred-social-preview.png)

[![Build](https://github.com/jacred-fdb/jacred/actions/workflows/build.yml/badge.svg)](https://github.com/jacred-fdb/jacred/actions/workflows/build.yml)
[![Release](https://github.com/jacred-fdb/jacred/actions/workflows/release.yml/badge.svg)](https://github.com/jacred-fdb/jacred/actions/workflows/release.yml)
[![GitHub release (latest SemVer)](https://img.shields.io/github/v/release/jacred-fdb/jacred?label=version)](https://github.com/jacred-fdb/jacred/releases)
[![GitHub tag (latest SemVer pre-release)](https://img.shields.io/github/v/tag/jacred-fdb/jacred?include_prereleases&label=pre-release)](https://github.com/jacred-fdb/jacred/tags)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Агрегатор торрент-трекеров с API в формате Jackett. Хранит данные в файловой БД (fdb), поддерживает синхронизацию с удалённой базой и самостоятельный парсинг трекеров по cron.

## Основные возможности

- 🔍 **Агрегация торрентов** с множества трекеров в единый API
- 📦 **Файловая БД (fdb)** для быстрого доступа к данным
- 🔄 **Синхронизация** с удалёнными серверами или самостоятельный парсинг
- 🎯 **API Jackett** — полная совместимость с форматом Jackett
- 📡 **Torznab XML** — встроенный Torznab API для Sonarr/Radarr/Prowlarr
- 🌐 **Веб-интерфейс** — поиск, статистика и редактор конфигурации
- ⚙️ **Настройки в браузере** — `/settings` (форма, YAML/JSON, валидация, diff перед сохранением)
- 📖 **OpenAPI / Swagger** — `/openapi.yaml`, интерактивная документация на `/swagger`
- 🗂️ **25 трекера** — парсинг и sync (см. [Трекеры и парсинг](docs/trackers-and-parsing.md))
- 🔐 **Поддержка прокси** и Tor для доступа к .onion доменам
- 📊 **Статистика** по трекерам и торрентам
- 🎵 **Модуль tracks** для сбора метаданных треков (опционально)
- ⚡ **Кеширование** для высокой производительности
- 🐳 **Docker** поддержка для простого развёртывания

## AI Документация

[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/jacred-fdb/jacred)

---

## Поддержать проект

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

## Быстрый старт

```bash
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | bash
```

Скрипт ставит приложение в **`/opt/jacred`**, создаёт systemd-сервис `jacred` и по желанию скачивает готовую базу.

Полезные опции: `--no-download-db`, `--pre-release`, `--update`, `--remove` (подробности — [установка](docs/installation.md)).

После установки:

- Конфиг: **`/opt/jacred/init.yaml`** или **`/opt/jacred/init.conf`**, либо веб-редактор **`/settings`** (LAN или `devkey` — см. [безопасность](docs/security.md))
- Веб-интерфейс: **`http://127.0.0.1:9117/`** (поиск), **`/stats`**, **`/settings`**
- Перезапуск: `systemctl restart jacred`
- Полный crontab для парсинга: `crontab /opt/jacred/Data/crontab`

> По умолчанию синхронизация отключена: скрипт скачивает базу, парсинг — по cron. Чтобы подтягивать базу с внешнего сервера, укажите `syncapi` в конфиге ([конфигурация](docs/configuration.md)).

Docker: [docs/docker.md](docs/docker.md).

---

## Документация

Онлайн: **[https://jacred-fdb.github.io/jacred/](https://jacred-fdb.github.io/jacred/)**

Полное оглавление: **[docs/README.md](docs/README.md)**.

| Документ | Описание |
| --- | --- |
| [Установка](docs/installation.md) | Скрипт, обновление, удаление |
| [Конфигурация](docs/configuration.md) | `init.yaml` / `init.conf`, sync, logging, search, Torznab |
| [Tracks](docs/tracks.md) | Модуль tracks (TorrServer / ffprobe) |
| [Трекеры и парсинг](docs/trackers-and-parsing.md) | Список трекеров, cron, .onion |
| [Безопасность](docs/security.md) | Политики доступа, `apikey` / `devkey` |
| [API](docs/api.md) | OpenAPI, эндпоинты, `/dev/*`, cron, maintenance |
| [Сборка](docs/building.md) | `make publish`, RID |
| [Docker](docs/docker.md) | Run / Compose и cron снаружи контейнера |
| [Решение проблем](docs/troubleshooting.md) | Типичные сбои |
| [Архитектура](docs/architecture.md) | Структура проекта и фоновые процессы |

Матрица доступа: [docs/access-matrix.md](docs/access-matrix.md). Веб-UI: [web/README.md](web/README.md).

---

## Лицензия

MIT License. См. файл [LICENSE](LICENSE) для подробностей.
