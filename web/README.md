# JacRed Web UI (Vue SPA)

Source of truth for the admin UI **and** static publish files (OpenAPI, PWA icons, manifest via VitePWA).

Vue 3 + Vite 8 + Tailwind v4 + shadcn-vue components (via `reka-ui`) + `@lucide/vue`.

CLI for new shadcn components is on-demand (`npm run ui:add -- button`), not a locked dependency.

## Dev

1. Start JacRed API (see proxy target in `vite.config.ts`).
2. In this folder:

```bash
npm ci
npm run gen:api   # public/openapi.yaml → src/lib/api/types.ts
npm run typecheck
npm run lint
npm run test
npm run dev
```

Vite proxies `/api`, `/stats/*`, `/health`, `/opensearch.xml` to the API.  
`/openapi.yaml` is served from [`public/openapi.yaml`](public/openapi.yaml).

## Production embed

`wwwroot/` is **not** in git. It is created only by the build:

```bash
# from repo root
./scripts/build-web-ui.sh   # recreates ../wwwroot from dist/
dotnet publish …
```

Also invoked from `Dockerfile`, `build.sh`, and CI. Runtime: ASP.NET serves API + SPA from `wwwroot/` on one port.

## Cloudflare Workers (optional)

Serve `dist/` on Cloudflare with a thin Worker that proxies API paths to a JacRed backend. The Vue app still uses same-origin `/api/...` calls — no CORS changes.

1. Copy env and set the backend origin:

```bash
cp .dev.vars.example .dev.vars
# edit JACRED_ORIGIN=https://your-jacred-host
```

2. Local preview (build + `wrangler dev`):

```bash
npm run preview:cf
```

3. Deploy:

```bash
# set production origin in the dashboard (Workers → Settings → Variables)
# or: echo -n 'https://your-jacred-host' | npx wrangler secret put JACRED_ORIGIN
npm run deploy:cf
```

Proxied paths: `/api/*`, `/stats/torrents`, `/stats/meta`, `/stats/tracks`, `/health`, `/version`, `/lastupdatedb`, `/opensearch.xml`, `/swagger`, `/swagger/*`.  
SPA routes (`/`, `/stats`, `/settings`) and static assets (`/openapi.yaml`, `/img/*`) stay on Workers Assets.

The Worker forwards `User-Agent`, `Referer`, `Origin`, API keys, and visitor IP (`CF-Connecting-IP` / `X-Forwarded-For` / `X-Real-IP`), and sets `X-JacRed-Client: jacred-web` + `Via`.

**Settings / Config API:** Cloudflare egress is not LAN. Configure `devkey` on JacRed and use **X-Dev-Key** in the UI when calling `/api/v1.0/config/*` remotely.

## Stack

See [docs/modern-web-ui-plan.md](../docs/modern-web-ui-plan.md).

## Useful scripts

```bash
npm run typecheck            # vue-tsc -b --noEmit
npm run lint                 # eslint src
npm run format               # eslint src --fix
npm run test                 # vitest run
npm run test:watch           # vitest
npm run preview              # vite preview
npm run preview:cf           # build + wrangler dev (needs .dev.vars)
npm run deploy:cf            # build + wrangler deploy
npm run generate-pwa-assets  # rebuild icons from public/img/jacred.png
npm run gen:api              # public/openapi.yaml → src/lib/api/types.ts
npm run ui:add -- button     # add a shadcn-vue component via npx
```

Locale: RU default, EN via header toggle (`jacredLocale` in localStorage).
Schema field labels from the API stay Russian.
