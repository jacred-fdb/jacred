---
title: Безопасность и доступ к API
description: Политики Public / ConfigApi / DevAdmin / ApiKeyWhenConfigured, apikey и devkey
tags:
  - security
  - api
---

# Безопасность и доступ к API

JacRed использует единый слой доступа: **`UseJacRedSecurity()`** (`SecurityHeadersMiddleware` + `JacRedAuthorizationMiddleware`). Политика определяется **только** по префиксу пути в `JacRedEndpointRegistry` — без атрибутов на контроллерах.

!!! info "Сеть: Peer IP vs Client IP"

    **Peer IP** — прямое TCP-подключение к Kestrel. **Client IP** из `CF-Connecting-IP` / `X-Real-IP` / `X-Forwarded-For` учитывается **только** если peer — loopback (cloudflared/nginx на том же хосте); иначе Client IP = peer. Если peer — private (loopback **или** RFC1918, напр. Traefik/nginx/Caddy в Docker `172.x`) **и** есть proxy identity headers (`X-Forwarded-For`, `X-Real-IP`, `X-Forwarded-Host`, `X-Forwarded-Proto`, `Forwarded`, `CF-*`, …), запрос **не** считается LAN-клиентом — нужен `devkey`. Прямой LAN/localhost **без** этих заголовков — по-прежнему без ключа. См. `ClientNetworkContext` / `JacRedAccessEvaluator`.

## Политики

| Политика | Правило | Ключи |
| --- | --- | --- |
| **Public** | Всегда разрешено (middleware) | — |
| **ConfigApi** | LAN-клиент **или** valid `devkey` | `X-Dev-Key`, `?devkey=` |
| **DevAdmin** | LAN-клиент **или** valid `devkey` | `X-Dev-Key`, `?devkey=` |
| **ApiKeyWhenConfigured** | Если `apikey` задан — требуется valid key; иначе открыто | `?apikey=`, `X-Api-Key`, `Bearer` |

**Коды отказа:** `OPTIONS` → 204; ключ настроен, но не передан → **401**; иначе → **403**.

!!! warning "Reverse proxy не заменяет `devkey`"

    **ConfigApi = DevAdmin** по сети: reverse proxy (same-host loopback **или** Docker/LAN peer с `X-Forwarded-*` / `X-Real-IP`) **сам по себе не заменяет** `devkey`. Нужен прямой LAN-клиент (RFC1918 / loopback **без** proxy identity headers) или заголовок/`?devkey=`.

## Префиксы путей → политика

| Префикс | Политика | Доп. проверка в контроллере |
| --- | --- | --- |
| `/dev/`, `/cron/`, `/jsondb` | DevAdmin | — |
| `/api/v1.0/config` | ConfigApi | — |
| `/`, `/stats`, `/settings` | Public | Vue SPA (`index.html`) |
| `/health`, `/health/background-jobs`, `/version`, `/lastupdatedb`, `/api/v1.0/conf` | Public | — |
| `/sync/*` | Public | `opensync` для данных sync |
| `/swagger`, `/openapi.yaml`, статика `/assets/` … | Public | `web: true` для UI |
| **Всё остальное** | ApiKeyWhenConfigured | `openstats` для `/stats/*` JSON |

## Доступ по контексту клиента

| Политика | Loopback / LAN без proxy headers | Reverse proxy (loopback или Docker `172.x` + XFF) без devkey | Интернет / удалённый прокси |
| --- | --- | --- | --- |
| Public | ✓ | ✓ | ✓ |
| ConfigApi | ✓ | ✗ | `devkey` |
| DevAdmin | ✓ | ✗ | `devkey` (если задан в конфиге) |
| ApiKeyWhenConfigured | `apikey` если задан | `apikey` если задан | `apikey` если задан |

## Белый список без `apikey` {#apikey}

Если в конфиге задан `apikey`, следующие пути **не требуют** его на уровне middleware:

`/`, `/stats`, `/settings`, `/health`, `/health/background-jobs`, `/version`, `/lastupdatedb`, `/openapi.yaml`, `/swagger`, `/api/v1.0/conf`, `/sync/*`

**Не входят:** `/cron/*`, `/dev/*`, `/jsondb/*`, `/api/v1.0/config/*`, поиск, Torznab, `/stats/torrents` и др.

## Ключи: `apikey` vs `devkey`

| Ключ | Назначение | Не заменяет |
| --- | --- | --- |
| `apikey` | Lampa, Sonarr, Prowlarr, публичный API | `devkey` для `/cron/*` |
| `devkey` | Админ: cron, dev, jsondb, config API извне | `apikey` для поиска |

Пример cron при обоих ключах:

```bash
curl -s -H "X-Api-Key: YOUR_API_KEY" -H "X-Dev-Key: YOUR_DEV_KEY" \
  "http://127.0.0.1:9117/cron/rutor/parse"
```

## Основные маршруты (краткая трассировка)

| Маршрут | Политика | Вторичный gate |
| --- | --- | --- |
| `GET /api/v2.0/indexers/.../results` | ApiKeyWhenConfigured | — |
| `GET /torznab/api` | ApiKeyWhenConfigured | — |
| `GET /api/v1.0/torrents` | ApiKeyWhenConfigured | — |
| `GET /api/v1.0/trackers` | ApiKeyWhenConfigured | — |
| `GET /stats/torrents`, `/stats/tracks`, `/stats/meta` | ApiKeyWhenConfigured | `openstats` |
| `GET /sync/fdb/torrents` | Public | `opensync` |
| `GET/POST /api/v1.0/config/*` | ConfigApi | — |
| `GET /cron/{tracker}/parse` | DevAdmin | — |
| `GET /jsondb/save` | DevAdmin | — |

## Матрица доступа

Полная трассировка маршрутов, политик и вторичных проверок — [`access-matrix.md`](access-matrix.md). Источник истины в коде: [`Infrastructure/Security/JacRedEndpointRegistry.cs`](https://github.com/jacred-fdb/jacred/blob/main/Infrastructure/Security/JacRedEndpointRegistry.cs).
