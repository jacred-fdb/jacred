# Torrserver API — как мы с ним работаем

Соответствие реализации в JacRed API Torrserver из `temp/TorrServer`. Эндпоинты требуют авторизации, если на сервере включён auth.

---

## 1. Добавление торрента (без сохранения в БД Torrserver)

**Эндпоинт:** `POST /torrents`  
**Тело (JSON):**

```json
{
  "action": "add",
  "link": "magnet:?xt=urn:btih:...",
  "save_to_db": false
}
```

| Поле          | Обязательное | Описание |
|---------------|--------------|----------|
| `action`      | да           | `"add"` |
| `link`        | да           | Magnet-ссылка или `http(s)://`, `file://`. Поддерживается и magnet. |
| `save_to_db`  | нет          | `false` — не сохранять торрент в БД Torrserver (мы так и шлём). |

**Ответ:** `200 OK`, в теле — JSON статуса торрента (TorrentStatus).  
**Реализация:** `TorrserverClient.AddTorrent()` — шлёт именно такой JSON.

---

## 2. Получение статуса торрента (проверка метаданных)

**Эндпоинт:** `POST /torrents`  
**Тело (JSON):**

```json
{
  "action": "get",
  "hash": "40-символьный infohash в hex (lowercase)"
}
```

| Поле     | Обязательное | Описание |
|----------|--------------|----------|
| `action` | да           | `"get"` |
| `hash`   | да           | Infohash торрента (hex, 40 символов). |

**Ответ:**  
- `200 OK` — в теле JSON с полями в т.ч. `file_stats` (список файлов). Когда метаданные получены, `file_stats` непустой.  
- `404` — торрент не найден.

**Реализация:** `TorrserverClient.GetTorrent()`. По 404 получаем `null`; по 200 десериализуем в `TorrserverTorrentStatus` (поля `hash`, `file_stats` и т.д.). Метаданные считаем готовыми, если `file_stats` есть и не пустой.

---

## 3. Удаление торрента

**Эндпоинт:** `POST /torrents`  
**Тело (JSON):**

```json
{
  "action": "rem",
  "hash": "40-символьный infohash в hex"
}
```

| Поле     | Обязательное | Описание |
|----------|--------------|----------|
| `action` | да           | `"rem"` |
| `hash`   | да           | Infohash торрента. |

**Ответ:** `200 OK` (тело пустое или не используется).  
**Реализация:** `TorrserverClient.RemTorrent()` — шлёт этот JSON, ответ не разбираем.

---

## 4. Метаданные ffprobe (video / audio / subtitles)

**Эндпоинт:** `GET /ffp/{hash}/{id}`  

| Параметр | Описание |
|----------|----------|
| `hash`   | Infohash торрента (hex). |
| `id`     | Номер файла в раздаче (1-based). Обычно берём первый файл — `1`. |

**Ответ:** `200 OK` — JSON в формате ffprobe (поля `streams`, при необходимости `format`, `chapters`). В `streams` — видео-, аудио- и субтитры.  
**Реализация:** `TorrserverClient.Ffp(baseUrl, hash, fileIndex: 1)` — запрос `GET {baseUrl}/ffp/{hash}/1`.

---

## Общее

- **Base URL** — из `tservers[].url` (например `http://127.0.0.1:8090`), без завершающего `/`.
- **Авторизация:** при необходимости передаём заголовок `Authorization: Basic <base64(user:password)>` (логин/пароль из `tservers[].username` / `tservers[].password`).
- **Content-Type:** для всех `POST /torrents` — `application/json`.
- Имена полей в JSON — **в нижнем регистре** (`action`, `link`, `hash`, `save_to_db`), как в документации Torrserver и в нашем клиенте.

Исходный код API Torrserver: `temp/TorrServer/server/web/api/torrents.go`, `temp/TorrServer/server/web/api/ffprobe.go`, `temp/TorrServer/server/web/api/route.go`.
