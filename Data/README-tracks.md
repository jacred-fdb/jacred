# Сбор метаданных треков (video / audio / subtitles)

Система получает метаданные торрентов (видео, аудио, субтитры) через API Torrserver (`/ffp`). Торрент на Torrserver добавляется с `save_to_db: false`, после получения данных удаляется. Обрабатываются только торренты, у которых метаданных ещё нет.

Конфиг: **init.yaml** или **init.conf** (в корне проекта). Ниже — готовые сценарии и пояснения.

---

## Рекомендуемый вариант: только новые торренты

**Самый частый сценарий:** получать метаданные только для тех торрентов, которые парсер только что подтянул с трекеров (новые за последние сутки). Не гоняем старый каталог — только свежие раздачи.

Готовый пример конфига: **[example-tracks-new-only.yaml](example-tracks-new-only.yaml)** (скопируйте нужные поля в свой `init.yaml` или используйте файл как ориентир).

**Минимальная настройка в init.yaml:**

```yaml
tracks: true
tracksOnlyNew: true
tracksDayWindowDays: 1
tracksWorkersDay: 2
tservers:
  - url: http://127.0.0.1:8090
```

| Параметр | Значение | Смысл |
|----------|----------|--------|
| `tracksOnlyNew` | `true` | Включена только задача «новые» (окно за последние N дней), без месяца/года/обновлений |
| `tracksDayWindowDays` | `1` | Берём торренты, созданные за последние 1 сутки (то, что парсер получил «сегодня») |
| `tracksWorkersDay` | `2` | Два воркера (можно поставить 1 или 3–5 при большом потоке новых раздач) |

После запуска JacRed будет подхватывать новые торренты из парсинга и по мере возможности запрашивать для них метаданные (video/audio/subtitles) через Torrserver.

---

## Что нужно перед запуском

1. **Torrserver** доступен по HTTP(S), с поддержкой эндпоинтов `/torrents` (add/get/rem) и `/ffp/{hash}/{id}`.
2. В конфиге задан массив **tservers** — один или несколько серверов (для каждого можно указать `username`/`password` при необходимости).

Пример минимального блока:

```yaml
tracks: true
tservers:
  - url: http://127.0.0.1:8090
```

---

## Сценарий 1: Метаданные за последний месяц

Обрабатывать торренты **без метаданных**, созданные за последние 30 дней. Один воркер по «дню», один по «месяцу».

**init.yaml:**

```yaml
tracks: true
tracksOnlyNew: false
tracksmod: 0
tracksDayWindowDays: 1
tracksMonthWindowDays: 30
tracksWorkersDay: 1
tracksWorkersMonth: 1
tracksWorkersYear: 1
tracksWorkersOlder: 1
tracksWorkersUpdates: 1
tservers:
  - url: http://127.0.0.1:8090
```

- **tracksDayWindowDays: 1** — задача «день»: торренты за последние 1 день.
- **tracksMonthWindowDays: 30** — задача «месяц»: торренты созданы от 1 до 30 дней назад.
- Итого обрабатываются торренты за последний месяц (у которых ещё нет метаданных).

**Только последний месяц, без года и старых:**

```yaml
tracks: true
tracksOnlyNew: false
tracksmod: 1
tracksDayWindowDays: 1
tracksMonthWindowDays: 30
tracksWorkersDay: 1
tracksWorkersMonth: 2
tservers:
  - url: http://127.0.0.1:8090
```

**tracksmod: 1** — включены только задачи «день» и «месяц», задачи «год» и «старые» отключены. Для «месяца» можно поднять **tracksWorkersMonth** (например 2), чтобы быстрее пройти последний месяц.

---

## Сценарий 2: Только новые торренты (подробно)

Тот же режим, что и в блоке выше: **только новые** торренты без метаданных за последний день (то, что парсер получил «сегодня»).

Полный пример: **[example-tracks-new-only.yaml](example-tracks-new-only.yaml)**.

**init.yaml:**

```yaml
tracks: true
tracksOnlyNew: true
tracksDayWindowDays: 1
tracksWorkersDay: 2
tservers:
  - url: http://127.0.0.1:8090
```

- **tracksOnlyNew: true** — запускается только задача по окну «день», задачи месяц/год/обновления не запускаются.
- **tracksDayWindowDays: 1** — берутся торренты с датой создания за последние 1 сутки (новые по парсеру).
- **tracksWorkersDay: 2** — два воркера для этой задачи (можно увеличить при большом потоке новых раздач).

Так вы обновляете метаданные только для тех торрентов, которые парсер добавил за последние сутки.

---

## Сценарий 3: Метаданные за последнюю неделю

Обрабатывать торренты без метаданных за последние 7 дней, только эта «недельная» задача.

**init.yaml:**

```yaml
tracks: true
tracksOnlyNew: true
tracksDayWindowDays: 7
tracksWorkersDay: 3
tservers:
  - url: http://127.0.0.1:8090
```

- **tracksDayWindowDays: 7** — окно «день» расширено до 7 дней (последняя неделя).
- **tracksOnlyNew: true** — другие задачи отключены.
- **tracksWorkersDay: 3** — три воркера для ускорения.

---

## Сценарий 4: Разовый прогон без смены конфига (API)

Можно один раз запустить сбор метаданных за N дней **без перезапуска приложения** и без правки конфига — через эндпоинт (только с localhost):

```text
GET http://127.0.0.1:9117/dev/TracksRunOnce?window=30
```

- **window=30** — обработать торренты без метаданных, созданные за последние 30 дней.
- **window=7** — за последнюю неделю.
- **window=1** — за последний день (по сути «новые за сегодня»).

Ответ сразу: `{ "ok": true, "queued": true, "windowDays": 30, "message": "..." }`. Сама обработка идёт в фоне.

Удобно, когда нужно «догнать» метаданные за месяц или неделю один раз, не меняя основные настройки треков.

---

## Сценарий 5: Несколько Torrserver и больше воркеров

Несколько серверов (с авторизацией на части из них) и больше воркеров по «дню» и «месяцу»:

**init.yaml:**

```yaml
tracks: true
tracksOnlyNew: false
tracksmod: 1
tracksDayWindowDays: 1
tracksMonthWindowDays: 30
tracksWorkersDay: 5
tracksWorkersMonth: 2
tservers:
  - url: http://127.0.0.1:8090
  - url: https://ts2.example.com
    username: myuser
    password: mypass
```

Сервер выбирается случайно при каждой обработке торрента. Для «дня» — 5 воркеров, для «месяца» — 2.

---

## Краткий справочник параметров

| Параметр | Описание | Пример |
|----------|----------|--------|
| **tracks** | Включить сбор метаданных | `true` |
| **tservers** | Список Torrserver (url, при необходимости username/password) | см. примеры выше |
| **tracksOnlyNew** | `true` — только задача по окну «день», без месяц/год/обновлений | `false` / `true` |
| **tracksmod** | `0` — все задачи; `1` — только день и месяц | `0` / `1` |
| **tracksDayWindowDays** | За последние сколько дней считать «день» (7 = неделя) | `1`, `7` |
| **tracksMonthWindowDays** | Окно «месяц»: созданы 1..N дней назад | `30` |
| **tracksYearWindowMonths** | Окно «год» (месяцев назад) | `12` |
| **tracksUpdatesWindowDays** | Окно «обновления»: обновлены за N дней | `30` |
| **tracksWorkersDay** … **tracksWorkersUpdates** | Число воркеров по задачам (1..20) | `1`, `2`, `5` |

Обрабатываются только торренты **без уже сохранённых метаданных**. Типы sport/tvshow/docuserial не обрабатываются.

---

## Эндпоинты (только localhost)

| Метод | URL | Назначение |
|-------|-----|------------|
| GET | `/dev/TracksConfig` | Текущая конфигурация треков (окна, воркеры, tservers, таймауты) |
| GET | `/dev/TracksRunOnce?window=7` | Разовый прогон: торренты без метаданных за последние `window` дней |

Порт по умолчанию — из **listenport** (часто 9117). Доступ только с `127.0.0.1`.

---

## Формат конфига (JSON)

Тот же смысл в **init.conf** (JSON):

```json
{
  "tracks": true,
  "tracksOnlyNew": true,
  "tracksDayWindowDays": 1,
  "tracksWorkersDay": 2,
  "tservers": [
    { "url": "http://127.0.0.1:8090" }
  ]
}
```

Для сценария «метаданные за последний месяц»:

```json
{
  "tracks": true,
  "tracksOnlyNew": false,
  "tracksmod": 1,
  "tracksDayWindowDays": 1,
  "tracksMonthWindowDays": 30,
  "tracksWorkersDay": 1,
  "tracksWorkersMonth": 2,
  "tservers": [
    { "url": "http://127.0.0.1:8090" }
  ]
}
```

После правки **init.yaml** или **init.conf** конфиг перечитывается периодически; для смены числа воркеров или списка tservers может потребоваться перезапуск приложения.
