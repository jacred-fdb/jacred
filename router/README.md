# Router Worker

Cloudflare Worker, работающий как умный обратный прокси/маршрутизатор: направляет запросы по хосту, пути и query-параметрам на разные источники (домашняя лаборатория, Tailscale, туннели, Pages, Vercel) с индивидуальными заголовками и политиками кэширования для каждого маршрута.

## Возможности

- ✅ Маршрутизация по хосту, пути и query-параметрам
- ✅ Поддержка нескольких типов источников (домашняя лаборатория, Tailscale, туннели, Pages, Vercel)
- ✅ Собственные заголовки для каждого маршрута с поддержкой шаблонных переменных
- ✅ Политики кэширования по маршрутам с TTL и заголовками Vary
- ✅ Поддержка перезаписи пути (path rewriting)
- ✅ Подстановочные символы в хосте и пути
- ✅ Обработка редиректов
- ✅ Передача тела запроса для POST/PUT/PATCH/DELETE
- ✅ Модульная структура кода с чётким разделением ответственности
- ✅ Подробные комментарии в коде (JSDoc)

## Архитектура

Воркер организован в модульную структуру для удобства поддержки и тестирования:

```text
src/
├── config/
│   └── constants.js          # Конфигурация маршрутов и константы
├── handlers/
│   └── request-handler.js    # Основная логика обработки запросов
├── utils/
│   ├── route-matcher.js      # Сопоставление маршрутов и построение URL
│   ├── request-builder.js    # Построение запроса с заголовками по маршруту
│   └── response.js           # Обработка ответов и кэширование
└── worker.js                 # Точка входа воркера
```

### Обзор модулей

- **`config/constants.js`**: Определения маршрутов, конфигурация по умолчанию и константы
- **`handlers/request-handler.js`**: Основная логика обработки запросов и обработка ошибок
- **`utils/route-matcher.js`**: Сопоставление по хосту/пути/query, построение URL, перезапись пути
- **`utils/request-builder.js`**: Формирование опций fetch с заголовками по маршруту и шаблонными переменными
- **`utils/response.js`**: Обработка ответов, применение заголовков кэша, обработка редиректов
- **`worker.js`**: Экспорт Cloudflare Worker и точка входа

## Установка

1. Установите зависимости:

   ```bash
   npm install
   ```

2. Настройте маршруты в `src/config/constants.js` (см. раздел «Конфигурация»).

3. Настройте воркер в `wrangler.jsonc`:

   ```jsonc
   {
     "name": "router",
     "main": "src/worker.js",
     "compatibility_date": "2026-01-20",
     "routes": [
       {
         "pattern": "router.example.com",
         "zone_name": "example.com",
         "custom_domain": true,
       },
     ],
     // ... остальные настройки
   }
   ```

4. Разверните на Cloudflare:

   ```bash
   npm run deploy
   ```

Или запустите локально для разработки:

```bash
npm run dev
```

## Конфигурация

### Конфигурация маршрутов

Маршруты задаются в `src/config/constants.js` в массиве `ROUTES`. Для каждого маршрута можно указать:

- **`name`**: Уникальный идентификатор маршрута (для логов и отладки)
- **`hostname`**: Хост для сопоставления (точное совпадение, подстановка `*` или поддомен `*.example.com`)
- **`path`**: Шаблон пути (поддерживаются подстановки: `*` — любые символы, `**` — сегменты пути)
- **`query`**: Требования к query-параметрам (необязательно)
- **`origin`**: URL целевого источника
- **`originType`**: Тип источника (`home-lab`, `tailscale`, `tunnel`, `pages`, `vercel` или свой)
- **`headers`**: Дополнительные заголовки запроса (с поддержкой шаблонных переменных)
- **`cache`**: Настройки кэширования
- **`pathRewrite`**: Правила перезаписи пути (необязательно)

### Примеры конфигурации маршрутов

#### API домашней лаборатории

```javascript
{
  name: 'home-lab-api',
  hostname: 'api.example.com',
  path: '/api/*',
  origin: 'https://home-lab.example.com',
  originType: 'home-lab',
  headers: {
    'X-Forwarded-Host': 'api.example.com',
    'X-Real-IP': '${CF-Connecting-IP}',
    'X-Forwarded-Proto': 'https',
  },
  cache: {
    enabled: true,
    ttl: 300, // 5 минут
    vary: ['Accept', 'Accept-Language'],
  },
}
```

#### Сервис Tailscale

```javascript
{
  name: 'tailscale-service',
  hostname: 'tailscale.example.com',
  path: '/*',
  origin: 'http://100.x.x.x:8080',
  originType: 'tailscale',
  headers: {
    'X-Forwarded-Proto': 'https',
    'X-Forwarded-Host': 'tailscale.example.com',
  },
  cache: {
    enabled: false,
  },
}
```

#### Cloudflare Pages

```javascript
{
  name: 'cloudflare-pages',
  hostname: 'pages.example.com',
  path: '/*',
  origin: 'https://your-project.pages.dev',
  originType: 'pages',
  headers: {},
  cache: {
    enabled: true,
    ttl: 3600, // 1 час
    staleWhileRevalidate: 86400, // 24 часа
  },
}
```

#### Приложение Vercel

```javascript
{
  name: 'vercel-app',
  hostname: 'vercel.example.com',
  path: '/*',
  origin: 'https://your-app.vercel.app',
  originType: 'vercel',
  headers: {
    'X-Forwarded-Host': 'vercel.example.com',
  },
  cache: {
    enabled: true,
    ttl: 1800, // 30 минут
  },
}
```

#### Сервис через туннель

```javascript
{
  name: 'tunnel-service',
  hostname: 'tunnel.example.com',
  path: '/*',
  origin: 'https://tunnel.example.com',
  originType: 'tunnel',
  headers: {},
  cache: {
    enabled: false,
  },
}
```

#### Перезапись пути

```javascript
{
  name: 'api-with-rewrite',
  hostname: 'api.example.com',
  path: '/v1/*',
  origin: 'https://backend.example.com',
  pathRewrite: {
    pattern: '^/v1',
    replacement: '/api/v1',
  },
  headers: {},
  cache: {
    enabled: true,
    ttl: 600,
  },
}
```

#### Сопоставление по query-параметрам

```javascript
{
  name: 'api-with-query',
  hostname: 'api.example.com',
  path: '/search',
  query: {
    version: 'v2',
    format: ['json', 'xml'], // Один из этих значений
  },
  origin: 'https://backend.example.com',
  headers: {},
  cache: {
    enabled: true,
    ttl: 300,
  },
}
```

#### Подстановка поддомена

```javascript
{
  name: 'wildcard-subdomain',
  hostname: '*.example.com', // Любой поддомен
  path: '/*',
  origin: 'https://default.example.com',
  headers: {},
  cache: {
    enabled: true,
    ttl: 3600,
  },
}
```

### Шаблонные переменные в заголовках

В заголовках можно использовать переменные, подставляемые при запросе:

- `${CF-Connecting-IP}`: Реальный IP клиента
- `${CF-IPCountry}`: Код страны клиента
- `${CF-Ray}`: Cloudflare Ray ID
- `${User-Agent}`: User-Agent из запроса
- Любое другое имя заголовка: значение из исходного запроса

Пример:

```javascript
headers: {
  'X-Real-IP': '${CF-Connecting-IP}',
  'X-Original-User-Agent': '${User-Agent}',
  'X-Country': '${CF-IPCountry}',
}
```

### Настройки кэширования

Параметры кэширования:

- **`enabled`**: Включить/выключить кэш (по умолчанию: `false`)
- **`ttl`**: Время жизни в секундах (по умолчанию: `0`)
- **`vary`**: Массив заголовков, по которым различается кэш (например, `['Accept', 'Accept-Language']`)
- **`staleWhileRevalidate`**: Время stale-while-revalidate в секундах (необязательно)
- **`staleIfError`**: Время stale-if-error в секундах (необязательно)

Пример:

```javascript
cache: {
  enabled: true,
  ttl: 3600, // 1 час
  vary: ['Accept', 'Accept-Language', 'Authorization'],
  staleWhileRevalidate: 86400, // 24 часа
  staleIfError: 3600, // 1 час
}
```

## Использование

### Сопоставление маршрутов

Маршруты проверяются по порядку — используется первый подходящий. Сопоставление идёт по:

1. **Хост**: точное совпадение, подстановка `*` или поддомен `*.example.com`
2. **Путь**: шаблон с подстановками (`*` — любые символы, `**` — сегменты пути)
3. **Query**: требования к query-параметрам (если заданы)

### Шаблоны путей

- `/*`: любой путь
- `/api/*`: пути, начинающиеся с `/api/`
- `/api/**`: `/api/` и все вложенные пути
- `/static/*.js`: JS-файлы в каталоге `/static/`
- `/users/:id`: не поддерживается (используйте `/users/*` и разбирайте на источнике)

### Примеры

#### Базовая маршрутизация

```bash
# Запрос на api.example.com/api/users
# Совпадение: hostname: 'api.example.com', path: '/api/*'
# Проксируется на: https://home-lab.example.com/api/users
curl "https://api.example.com/api/users"
```

#### Пример перезаписи пути

```bash
# Запрос на api.example.com/v1/users
# Маршрут с pathRewrite: { pattern: '^/v1', replacement: '/api/v1' }
# Проксируется на: https://backend.example.com/api/v1/users
curl "https://api.example.com/v1/users"
```

#### Пример сопоставления по query

```bash
# Запрос на api.example.com/search?version=v2&format=json
# Маршрут с query: { version: 'v2', format: ['json', 'xml'] }
curl "https://api.example.com/search?version=v2&format=json"
```

## Разработка

### Локальная разработка

1. **Закомментируйте маршруты в `wrangler.jsonc`** для локальных проверок:

   Wrangler dev может мешать сопоставлению маршрутов, когда они заданы в конфиге. Для локального тестирования временно закомментируйте блок routes:

   ```jsonc
   "routes": [
     // Закомментировано для локального тестирования
     // {
     //   "pattern": "keenetic.torrservera.net",
     //   "zone_name": "torrservera.net",
     //   "custom_domain": true,
     // },
     // {
     //   "pattern": "terraform.torrservera.net",
     //   "zone_name": "torrservera.net",
     //   "custom_domain": true,
     // },
   ],
   ```

   **Примечание**: Маршруты в `wrangler.jsonc` нужны только для деплоя. При локальном тестировании маршрутизацию полностью задают маршруты в `src/config/constants.js`.

2. Запустите локальный сервер разработки:

   ```bash
   npm run dev
   ```

3. Проверяйте маршруты, передавая заголовок `Host`:

```bash
# Проверка маршрута — заголовок Host должен соответствовать hostname маршрута
curl "http://localhost:8787/" -H "Host: api.example.com"

# Пример: маршрут Keenetic
curl -v "http://localhost:8787/" -H "Host: keenetic.torrservera.net"

# Пример: маршрут Terraform Registry
curl -v "http://localhost:8787/" -H "Host: terraform.torrservera.net"
```

**Альтернатива**: если не хотите комментировать маршруты, можно использовать query-параметр переопределения:

```bash
# Тест с переопределением через query
curl "http://localhost:8787/?__host=terraform.torrservera.net"
curl "http://localhost:8787/?__host=keenetic.torrservera.net"
```

### Проверка маршрутов

Маршруты можно проверять через curl или любой HTTP-клиент:

```bash
# Маршрут домашней лаборатории
curl "https://api.example.com/api/users" \
  -H "X-Custom-Header: value"

# POST-запрос
curl -X POST "https://api.example.com/api/users" \
  -H "Content-Type: application/json" \
  -d '{"name": "John", "email": "john@example.com"}'

# Проверка кэширования
curl -v "https://pages.example.com/" \
  -H "Accept: text/html" \
  -H "Accept-Language: en-US"
```

## Обработка ошибок

### Формат ответа с ошибкой

Ошибки возвращаются в виде JSON:

```json
{
  "error": true,
  "message": "Текст ошибки",
  "status": 404
}
```

### Типичные коды ошибок

- `400`: Bad Request (неверная конфигурация маршрута)
- `404`: Not Found (маршрут не найден)
- `502`: Bad Gateway (ошибка подключения к источнику)
- `503`: Service Unavailable
- `504`: Gateway Timeout (таймаут источника)
- `500`: Internal Server Error

## Структура кода

### Поток обработки запроса

1. Запрос попадает в `worker.js`
2. `handlers/request-handler.js` обрабатывает запрос:
   - разбирает URL (хост, путь, query);
   - находит подходящий маршрут через `utils/route-matcher.js`;
   - строит целевой URL (с перезаписью пути при необходимости);
   - формирует опции fetch в `utils/request-builder.js`:
     - подставляет заголовки маршрута и шаблонные переменные;
     - пробрасывает разрешённые заголовки из конфига;
     - обрабатывает тело запроса;
   - выполняет прокси-запрос;
   - обрабатывает ответ в `utils/response.js`:
     - применяет заголовки кэша по конфигурации маршрута;
     - обрабатывает редиректы;
     - копирует нужные заголовки от источника;
   - возвращает ответ.

### Зависимости модулей

- `worker.js` → `handlers/request-handler.js`
- `request-handler.js` → `utils/route-matcher.js`, `utils/request-builder.js`, `utils/response.js`
- `route-matcher.js` → `config/constants.js`
- `request-builder.js` → `config/constants.js`
- `response.js` → `config/constants.js`

## Рекомендации

1. **Порядок маршрутов**: размещайте более специфичные маршруты выше общих (например, `/api/v2/*` перед `/api/*`).

2. **Кэширование**: включайте кэш для статики и API-ответов, которые редко меняются.

3. **Заголовки**: используйте шаблонные переменные для динамических заголовков (IP и т.п.).

4. **Ошибки**: следите за логами воркера при проблемах с сопоставлением маршрутов.

5. **Безопасность**: для чувствительных маршрутов рассмотрите добавление заголовков аутентификации/авторизации.

6. **Производительность**: подбирайте TTL кэша, чтобы балансировать актуальность и скорость.

## Ограничения

- Маршруты проверяются последовательно (выигрывает первое совпадение).
- В шаблонах путей нет именованных параметров (используйте подстановки и разбор на источнике).
- Сопоставление по query — базовое (точное совпадение, подстановка или массив значений).
- Кэш использует Cloudflare Cache API (в рамках лимитов Cloudflare).
- Размер запроса ограничен лимитами Cloudflare Workers.

## Решение проблем

### Маршрут не срабатывает

- Проверьте точное совпадение хоста, пути и query-параметров.
- Убедитесь в правильном порядке маршрутов (более специфичные — выше).
- Проверьте опечатки в конфигурации маршрута.

### Заголовки не применяются

- Проверьте имена заголовков (учёт регистра).
- Проверьте синтаксис шаблонных переменных (`${VAR_NAME}`).
- Убедитесь, что значения заголовков — строки.

### Кэш не работает

- Убедитесь, что `cache.enabled` равно `true`.
- Проверьте, что `cache.ttl` больше 0.
- Учтите, что кэшируются только GET-запросы.
- Проверьте доступность Cloudflare Cache API.

### Ошибки подключения к источнику

- Проверьте корректность и доступность URL источника.
- Проверьте файрвол и сетевые ограничения.
- Убедитесь, что источник отвечает (проверьте напрямую).
- Проверьте настройки таймаута для медленных источников.
