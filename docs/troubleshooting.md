---
title: Решение проблем
description: Типичные сбои JacRed — ICU, cron, API, прокси, память, трекеры
tags:
  - ops
  - troubleshooting
---

# Решение проблем

??? failure "Приложение не запускается"

    - **Ошибка «Couldn't find a valid ICU package»** — .NET требует библиотеку ICU на Linux. Установите: `apt install libicu-dev` (Debian/Ubuntu) или `libicu76` / `libicu72` (имя зависит от версии). Проверьте доступные пакеты: `apt-cache search libicu`. Подробнее: [aka.ms/dotnet-missing-libicu](https://aka.ms/dotnet-missing-libicu)
    - Проверьте наличие конфигурационного файла (`init.yaml` или `init.conf`)
    - Убедитесь, что порт не занят другим процессом: `netstat -tuln | grep 9117`
    - Проверьте логи systemd: `journalctl -u jacred -f`
    - Для Docker: проверьте логи контейнера: `docker logs jacred`

??? failure "База данных не обновляется"

    - Проверьте, что cron настроен правильно: `crontab -l` (на **хосте** или в отдельном контейнере с планировщиком; **внутри** образа JacRed cron нет)
    - Для Docker: убедитесь, что по расписанию вызываются **`/cron/...`** и при необходимости **`/jsondb/save`**, с учётом **`apikey`** / **`devkey`** в `curl`, если они заданы в конфиге
    - Убедитесь, что `syncapi` указан корректно (если используется синхронизация)
    - Проверьте логи парсеров: `tail -f Data/log/{tracker}.log`
    - Убедитесь, что трекер доступен и учётные данные верны
    - **Конфиг не подхватывается:** рабочий файл — `./init.yaml` (CWD рядом с бинарником); правка только `Data/init.yaml` без копии/symlink в корень не применяется
    - **Korsars:** в логе `login.u empty` / `login failed` — задайте `Korsars.login` или `cookie` с `bb_data` в корневом `init.yaml`
    - **Anistar:** пустой parse / `empty` — проверьте `Anistar.alias` (должен быть рабочее зеркало, напр. `https://v30.astar.bz`). `host` только для FDB-ссылок, через него не ходим. В логе: `Page fetch failed` / `rqHost`
    - **Anibelka:** не логиньтесь — анонимный download
    - **Rutracker / Cloudflare:** проверьте, что FlareSolverr доступен (`curl http://127.0.0.1:8191/` или `http://flaresolverr:8191/` в compose), в конфиге `flaresolverr.enable: true` и верный `url`, и что срабатывает warmup: `curl http://127.0.0.1:9117/cron/cloudflare/Warmup` (первый ответ может занять до ~180 с). Если на VPS challenge детектится, но не решается — задайте residential/ISP `PROXY_*` у контейнера FlareSolverr (см. playbook в [Rutracker README](https://github.com/jacred-fdb/jacred/blob/main/Infrastructure/Trackers/Rutracker/README.md)). Smoke: [`./scripts/cron_rutracker_smoke.sh`](https://github.com/jacred-fdb/jacred/blob/main/scripts/cron_rutracker_smoke.sh)

??? failure "API не отвечает"

    - Проверьте, что приложение запущено: `systemctl status jacred`
    - Проверьте health endpoint: `curl http://localhost:9117/health`
    - Убедитесь, что `apikey` указан правильно (если используется авторизация)
    - Проверьте настройки `listenip` и `listenport` в конфиге

??? tip "Проблемы с прокси/Tor"

    - Убедитесь, что Tor запущен на порту 9050: `netstat -tuln | grep 9050`
    - Проверьте правильность регулярного выражения в `globalproxy.pattern`
    - Убедитесь, что формат прокси корректен: `socks5://127.0.0.1:9050`
    - Проверьте логи для ошибок подключения

??? tip "Высокое потребление памяти"

    - Включите `evercache` для оптимизации работы с файлами
    - Уменьшите `maxreadfile` в конфиге
    - Настройте ротацию логов через `logFdbRetentionDays`, `logFdbMaxSizeMb`, `logFdbMaxFiles`
    - Для Docker: увеличьте лимит памяти в `deploy.resources.limits.memory`
    - FlareSolverr держит ~600–700 МБ на сессию Chromium; при простое сессия закрывается через `flaresolverr.sessionIdleMinutes` (по умолчанию 30)
