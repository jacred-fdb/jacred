---
title: Документация JacRed
description: Операторская документация JacRed — установка, конфигурация, API, безопасность
tags:
  - start
---

# Документация JacRed

Агрегатор торрент-трекеров с API Jackett, файловой БД (fdb), sync и самостоятельным парсингом.

<div class="hero-cta" markdown>

[Установка](installation.md){ .md-button .md-button--primary }
[Docker](docker.md){ .md-button }

</div>

<div class="grid cards" markdown>

-   :material-download: **Установка**

    ---

    Скрипт `jacred.sh`, systemd, обновление и удаление

    [:octicons-arrow-right-24: Перейти](installation.md)

-   :material-docker: **Docker**

    ---

    Run / Compose, тома и cron снаружи контейнера

    [:octicons-arrow-right-24: Перейти](docker.md)

-   :material-cog: **Конфигурация**

    ---

    `init.yaml` / `init.conf`, sync, search, Torznab

    [:octicons-arrow-right-24: Перейти](configuration.md)

-   :material-shield-lock: **Безопасность**

    ---

    Политики доступа, `apikey` / `devkey`, LAN и proxy

    [:octicons-arrow-right-24: Перейти](security.md)

-   :material-api: **API**

    ---

    OpenAPI, поиск, Config API, `/dev/*`, cron, maintenance

    [:octicons-arrow-right-24: Перейти](api.md)

-   :material-music-note: **Tracks**

    ---

    TorrServer / ffprobe, concurrency и troubleshooting

    [:octicons-arrow-right-24: Перейти](tracks.md)

-   :material-bug: **Решение проблем**

    ---

    ICU, cron, прокси, память, трекеры

    [:octicons-arrow-right-24: Перейти](troubleshooting.md)

-   :material-sitemap: **Архитектура**

    ---

    Слои проекта и фоновые процессы

    [:octicons-arrow-right-24: Перейти](architecture.md)

</div>

!!! tip "Связанные README вне сайта"

    - [web/README.md](https://github.com/jacred-fdb/jacred/blob/main/web/README.md) — Vue SPA
    - [Infrastructure/Trackers/Rutracker/README.md](https://github.com/jacred-fdb/jacred/blob/main/Infrastructure/Trackers/Rutracker/README.md) — Rutracker / FlareSolverr
