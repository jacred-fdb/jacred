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
npm run generate-pwa-assets  # rebuild icons from public/img/jacred.png
npm run gen:api              # public/openapi.yaml → src/lib/api/types.ts
npm run ui:add -- button     # add a shadcn-vue component via npx
```

Locale: RU default, EN via header toggle (`jacredLocale` in localStorage).
Schema field labels from the API stay Russian.
