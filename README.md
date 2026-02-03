# JacRed

[![Build](https://github.com/jacred-fdb/jacred/actions/workflows/build.yml/badge.svg)](https://github.com/jacred-fdb/jacred/actions/workflows/build.yml)

## AI Документация

[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/jacred-fdb/jacred)

## Установка

Установка одной командой (запускать от любого пользователя, при необходимости запросится sudo):

```bash
curl -s https://raw.githubusercontent.com/jacred-fdb/jacred/main/jacred.sh | bash
```

Скрипт устанавливает приложение в `/home/jacred`, .NET 9.0, systemd-сервис `jacred`, cron для сохранения БД и при первом запуске по желанию скачивает готовую базу.

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
sudo /home/jacred/jacred.sh --update
# или после загрузки скрипта:
./jacred.sh --update

# Удаление
sudo ./jacred.sh --remove
```

Установка/удаление под конкретным пользователем (cron будет добавлен или удалён для этого пользователя):

```bash
sudo -u myservice ./jacred.sh
sudo -u myservice ./jacred.sh --update
sudo -u myservice ./jacred.sh --remove
```

После установки: настройте `/home/jacred/init.conf`, перезапуск — `systemctl restart jacred`. Полный crontab для парсинга — `crontab /home/jacred/Data/crontab`.

> [!IMPORTANT]  
> По умолчанию настроена синхронизация базы с внешнего сервера.

## Docker

<https://github.com/pavelpikta/docker-jacred-fdb>

## Источники

Kinozal, Nnmclub, Rutor, Torrentby, Bitru, Rutracker, Megapeer, Selezen, Toloka (UKR), Baibako, LostFilm, Animelayer

## Самостоятельный парсинг источников

1. Настроить init.conf (пример настроек в example.conf)
2. Перенести в crontab "Data/crontab" или указать сервер "syncapi" в init.conf

## Доступ к доменам .onion

1. Запустить tor на порту 9050
2. В init.conf указать .onion домен в host

## Параметры init.conf

* apikey - включение авторизации по ключу
* mergeduplicates - объединять дубликаты в выдаче
* openstats - открыть доступ к статистике
* opensync - разрешить синхронизацию с базой через syncapi
* syncapi - источник с открытым opensync для синхронизации базы
* timeSync - интервал синхронизации с базой syncapi
* maxreadfile - максимальное количество открытых файлов за один поисковый запрос
* evercache - хранить открытые файлы в кеше (рекомендуется для общего доступа с высокой нагрузкой)
* timeStatsUpdate - интервал обновления статистики в минутах

## Пример init.conf

* Список всех параметров, а так же значения по умолчанию смотреть в example.conf
* В init.conf нужно указывать только те параметры, которые хотите изменить

```json
{
  "listenport": 9120, // изменили порт
  "NNMClub": {        // изменили домен на адрес из сети tor 
    "alias": "http://nnmclub2vvjqzjne6q4rrozkkkdmlvnrcsyes2bbkm7e5ut2aproy4id.onion"
  },
  "globalproxy": [
    {
      "pattern": "\\.onion",  // запросы на домены .onion отправить через прокси
      "list": [
        "socks5://127.0.0.1:9050" // прокси сервер tor
      ]
    }
  ]
}
```
