# JacRed Modern Web UI — Rewrite Plan

> Replace the Bootstrap/vanilla `wwwroot` UI with a **fully new** SPA. Keep the **.NET JacRed API** as the backend.

**Chosen stack (primary)**

| Layer | Choice |
|--------|--------|
| UI runtime | **Vue 3** (`<script setup>` + Composition API) |
| Bundler | **Vite 8** (Rolldown) |
| Language | **TypeScript 6** (7.x optional later) |
| Components | **shadcn-vue** (main UI system) |
| CSS | **Tailwind CSS v4** (`@tailwindcss/vite`) |
| Primitives | **Reka UI** (via shadcn-vue) |
| Motion | **GSAP** (+ ScrollTrigger where useful) |
| Server state | **TanStack Query** (`@tanstack/vue-query`) |
| Icons | **lucide-vue-next** |
| Toasts | **vue-sonner** (shadcn-vue Sonner) |
| Routing | **Vue Router 4** |
| PWA / SW | **vite-plugin-pwa** (Workbox) + Vue register helper |
| API types | **openapi-typescript** from `openapi.yaml` |

**Hosting (recommended): embed Vite build into the .NET artifact** — see [§3](#3-hosting-embed-in-dotnet-vs-separate).

---

## 1. Current state (`wwwroot`) — what we replace

| Area | Today |
|------|--------|
| Pages | `index.html`, `stats.html`, `settings.html` |
| CSS | Bootstrap 5.3 + ~4.8k-line `styles.css` |
| JS | Vanilla IIFEs (~3.8k lines), no bundler/TS |
| Motion | GSAP (vendor) |
| PWA | `manifest.json` + custom `sw.js` |
| Host | `HomeController` serves HTML; static files from `wwwroot` |

**Parity features (behavior, not look):**

- Search: query, exact mode, sort, filters, URL sync, load-more, card/list, magnet/hash/TorrServer, API key
- Stats: tracker grid, sort/search, meta/tracks
- Settings: schema form + raw YAML/JSON, validate/diff/format/save, Dev key / LAN access
- Theme, PWA offline shell, Russian UI, OpenSearch (stays on .NET)

Greenfield UI: **shadcn-vue + JacRed brand tokens**, not a Bootstrap port.

---

## 2. Why Vue + shadcn-vue as main

| Decision | Rationale |
|----------|-----------|
| **shadcn-vue is the design system** | Copy-in components you own (`components/ui/*`); full control, Tailwind v4 / OKLCH, accessible via Reka UI. |
| **Vue 3 + Vite 8** | Fast SPA, no Node in production; ideal next to ASP.NET same-origin API. |
| **Not HeroUI / React** | Stack choice is Vue-first; shadcn-vue is the canonical component layer. |
| **Not Nuxt (v1)** | SSR not needed for self-hosted JacRed; plain Vite SPA keeps Docker = one .NET process. |
| **GSAP kept** | Page/result choreography only; control-level motion stays CSS / shadcn defaults. |
| **vue-query + vue-sonner + lucide-vue-next** | Vue equivalents of the modern React toolchain. |

**Scaffold reference:** [shadcn-vue Vite + Tailwind v4](https://www.shadcn-vue.com/docs/installation/vite)

```bash
npm create vite@latest web -- --template vue-ts
# add tailwindcss + @tailwindcss/vite
# configure @/* alias in tsconfig.json AND tsconfig.app.json
npx shadcn-vue@latest init
npx shadcn-vue@latest add button input select switch tabs dialog sheet badge tooltip sonner
```

---

## 3. Hosting: embed in .NET vs separate

### Option A — Embed in .NET build artifact (**recommended**)

```
CI / Docker:
  cd web && npm ci && npm run build   → dist/
  copy/merge into wwwroot
  dotnet publish → one artifact / image
Runtime:
  ASP.NET serves API + SPA on one port (e.g. 9117)
```

| Pros | Cons |
|------|------|
| One install, one Docker image, one port | Node at **build** time only |
| No CORS; tunnels/API key work as today | UI ships with app releases |
| Matches `jacred.sh` / current ops | — |

### Option B — Separate frontend

Only if you later need an independent CDN/marketing host. Adds CORS, dual deploys, weaker PWA story. **Not for core JacRed.**

### Dev workflow (Option A)

```ts
// vite.config.ts (sketch)
import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: { alias: { '@': path.resolve(__dirname, './src') } },
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:9117',
      '/stats/torrents': 'http://127.0.0.1:9117',
      '/stats/meta': 'http://127.0.0.1:9117',
      '/stats/tracks': 'http://127.0.0.1:9117',
      '/health': 'http://127.0.0.1:9117',
      '/opensearch.xml': 'http://127.0.0.1:9117',
    },
  },
  build: { outDir: 'dist', emptyOutDir: true },
})
```

CI copies `web/dist` → publish `wwwroot` (merge carefully so `openapi.yaml` / tracker icons are not wiped — put statics in `web/public`).

---

## 4. Architecture

```
┌──────────────────────────────────────────────┐
│  Browser PWA                                 │
│  Vue 3 + shadcn-vue + vue-query + GSAP       │
│  Routes: /  /stats  /settings                │
└──────────────────┬───────────────────────────┘
                   │ same-origin fetch
                   ▼
┌──────────────────────────────────────────────┐
│  ASP.NET JacRed                              │
│  API + Torznab + Sync + OpenSearch           │
│  SPA static files from wwwroot               │
└──────────────────────────────────────────────┘
```

**.NET touches:**

1. SPA fallback: `/`, `/stats`, `/settings` → `wwwroot/index.html`
2. Hashed `/assets/*` long-cache; HTML `no-store`
3. Docker **build** stage: Node 22 → `npm ci && npm run build` → copy into publish
4. Runtime: **no Node**

---

## 5. Project structure

```
web/
  package.json
  vite.config.ts
  components.json          # shadcn-vue
  tsconfig.json
  tsconfig.app.json
  index.html
  public/
    img/                   # brand + /img/ico/*.ico
    manifest.webmanifest
  src/
    main.ts
    App.vue
    style.css              # @import "tailwindcss"; theme tokens
    router/index.ts
    pages/
      SearchPage.vue
      StatsPage.vue
      SettingsPage.vue
    components/
      layout/AppShell.vue
      search/...
      stats/...
      settings/...
      ui/                  # shadcn-vue generated (owned source)
    lib/
      api/client.ts
      api/types.ts         # openapi-typescript
      magnets.ts
      storage.ts
      utils.ts             # cn() from shadcn
    composables/
      useTorrents.ts
      useStats.ts
      useConfig.ts
      useTheme.ts
    motion/
      gsap.ts
```

---

## 6. shadcn-vue component map

| Current UI | shadcn-vue |
|------------|------------|
| Nav actions | `Button` (ghost/icon) |
| Search field | `Input` + `Button` |
| Filters | `Sheet` (mobile) + `Collapsible` / panel |
| Sort | `ToggleGroup` or radio-styled `Button` group |
| Exact search | `Switch` |
| API key / TorrServer / shortcuts | `Dialog` |
| Results actions | `Button` + `Tooltip` + `Badge` |
| Toasts | `Sonner` (`vue-sonner`) |
| Settings Form / YAML | `Tabs` |
| Diff preview | `Dialog` + `ScrollArea` |
| Stats sort | `Select` |
| Theme | class/`data-theme` + CSS variables (shadcn OKLCH) |

Own the generated files under `components/ui` — customize JacRed accent/surfaces there and in `style.css` `@theme`.

---

## 7. Data layer

1. `openapi-typescript` → `src/lib/api/types.ts`
2. Typed `apiClient` (API key / Dev key headers, timeouts, 401 handling)
3. `@tanstack/vue-query` for torrents, stats, config (+ mutations)
4. Vue Router query as URL source of truth (`?s=&sort=&…`)
5. `localStorage` helpers for theme, list view, TorrServer, filter panel

---

## 8. PWA & service worker — use `vite-plugin-pwa`

**Yes — adopt [`vite-plugin-pwa`](https://vite-pwa-org.netlify.app/) as the main PWA package.**  
It replaces the hand-maintained `wwwroot/sw.js` + `manifest.json` + `js/pwa.js` with Workbox-generated precache that tracks **hashed Vite assets** (the hard part of today’s custom SW).

### Why not keep the custom `sw.js`?

| Today (`wwwroot/sw.js`) | With `vite-plugin-pwa` |
|-------------------------|-------------------------|
| Manual `CRITICAL_PRECACHE` / `OPTIONAL_PRECACHE` path lists | Precache manifest auto-built from Vite `dist` |
| Cache name bumped by scripts (`bump-sw-cache.sh`) | Workbox revision hashes invalidate automatically |
| Separate HTML shells cached per route | One SPA `index.html` + navigateFallback |
| Easy to miss new chunks after a redesign | Build-integrated; CI can’t ship without SW matching assets |

Keep **app-level** “server down” UX in Vue (port of `offline-inline.js` → `/health` probe). That is not the SW’s job.

### Recommended packages

| Package | Role |
|---------|------|
| **`vite-plugin-pwa`** | Manifest + SW generation, Workbox, inject into `index.html` |
| **`virtual:pwa-register/vue`** | Vue composable: register SW, `needRefresh` / `offlineReady` prompts |
| **`@vite-pwa/assets-generator`** (optional) | Generate icons / apple touch from one source (`jacred.png`) |

No need for a second SW framework (Serwist, Workbox CLI alone, etc.) unless `vite-plugin-pwa` hits a hard limit.

### Strategy choice

| Mode | When |
|------|------|
| **`generateSW`** (default) | Enough for most JacRed needs: precache app shell, runtimeCaching rules in config |
| **`injectManifest`** | If we must port custom logic (special offline HTML, migrate-from-old-caches, CSP-safe offline form) into `src/sw.ts` |

**Recommendation:** start with **`generateSW` + `navigateFallback`**. Switch to **`injectManifest`** only if offline parity needs the current minimal offline HTML / migration helpers.

### Config sketch (JacRed)

```ts
// vite.config.ts
import { VitePWA } from 'vite-plugin-pwa'

VitePWA({
  registerType: 'prompt', // or 'autoUpdate' — prefer prompt so users aren’t mid-search when SW swaps
  includeAssets: ['img/favicon.ico', 'img/icon-192.png', 'img/icon-512.png', 'img/icon-maskable-512.png'],
  manifest: {
    id: '/',
    name: 'JacRed — Поиск торрентов',
    short_name: 'JacRed',
    description: 'Торрент агрегатор. Поиск торрентов и статистика трекеров',
    lang: 'ru',
    start_url: '/',
    scope: '/',
    display: 'standalone',
    background_color: '#0a0a0f',
    theme_color: '#0a0a0f',
    orientation: 'any',
    categories: ['utilities', 'entertainment'],
    icons: [
      { src: 'img/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
      { src: 'img/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
      { src: 'img/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    ],
    shortcuts: [
      { name: 'Поиск торрентов', short_name: 'Поиск', url: '/', icons: [{ src: 'img/icon-192.png', sizes: '192x192', type: 'image/png' }] },
      { name: 'Статистика трекеров', short_name: 'Статистика', url: '/stats', icons: [{ src: 'img/icon-192.png', sizes: '192x192', type: 'image/png' }] },
    ],
  },
  workbox: {
    navigateFallback: '/index.html',
    navigateFallbackDenylist: [/^\/api\//, /^\/swagger/, /^\/torznab/, /^\/sync\//, /^\/health/, /^\/opensearch\.xml/],
    globPatterns: ['**/*.{js,css,html,ico,png,svg,woff2,webmanifest}'],
    runtimeCaching: [
      {
        // Tracker icons & fonts — cache-first
        urlPattern: ({ url }) => url.pathname.startsWith('/img/') || url.pathname.startsWith('/fonts/'),
        handler: 'CacheFirst',
        options: {
          cacheName: 'jacred-static',
          expiration: { maxEntries: 200, maxAgeSeconds: 60 * 60 * 24 * 30 },
        },
      },
      {
        // Never treat JSON API as offline app data (stale torrents are worse than empty)
        urlPattern: ({ url }) =>
          url.pathname.startsWith('/api/') ||
          url.pathname.startsWith('/stats/torrents') ||
          url.pathname.startsWith('/stats/meta') ||
          url.pathname.startsWith('/stats/tracks'),
        handler: 'NetworkOnly',
      },
    ],
  },
  devOptions: { enabled: true }, // optional: test SW in vite dev
})
```

### Vue app integration

```ts
// main.ts / composable
import { useRegisterSW } from 'virtual:pwa-register/vue'

const { needRefresh, updateServiceWorker } = useRegisterSW()
// show shadcn-vue Dialog / sonner: "Доступна новая версия" → updateServiceWorker()
```

Also port:

- Theme-aware `theme-color` meta (today `theme.js`) — still in app code, not SW
- `/health` probe banner when online but JacRed API is down (today `offline-inline.js`)
- Drop legacy `bump-sw-cache.sh` once Workbox revisions own cache busting

### Caching policy (product rules)

| Resource | Strategy |
|----------|----------|
| JS/CSS/HTML app shell | Precache (Workbox) |
| Tracker `.ico`, fonts, brand images | CacheFirst + expiration |
| `/api/*`, `/stats/torrents|meta|tracks` | **NetworkOnly** (no stale search/stats) |
| `/health` | NetworkOnly (used by online banner) |
| SPA navigations `/`, `/stats`, `/settings` | `navigateFallback` → cached `index.html` |

### ASP.NET / Docker notes

- Serve generated `sw.js` / `workbox-*.js` / `manifest.webmanifest` from `wwwroot` with correct MIME types (already fine via static files).
- Do **not** put SW under aggressive CDN cache without revision URLs — Workbox file names are hashed; HTML registration must stay fresh (`no-store` on `index.html` as today).
- Remove old `wwwroot/sw.js`, `manifest.json`, `js/pwa.js` after cutover so two SWs never fight.

### Phase placement

- Wire **`vite-plugin-pwa` in Phase 0** (manifest + basic precache) so every build is installable.
- Polish update prompt + `/health` banner in **Phase 4**; delete legacy SW scripts then.

---

## 9. Build & Docker (embed)

```dockerfile
# build stage (sketch)
RUN apk add --no-cache nodejs npm
WORKDIR /src/web
RUN npm ci && npm run build
# copy dist into /src/wwwroot before dotnet publish
```

- `build.sh` / CI: fail if `web` build fails
- Optional MSBuild `NpmBuild` before Publish
- Ignore `web/node_modules`, `web/dist`; prefer CI-produced wwwroot assets

---

## 10. Phases

0. **Scaffold** — Vue 3 + Vite 8 + TS + Tailwind v4 + shadcn-vue; **`vite-plugin-pwa`** (manifest + generateSW); AppShell; API client; Docker/CI  
1. **Search** — full parity + GSAP results motion; cut `/` to SPA  
2. **Stats** — tracker grid  
3. **Settings** — schema form + raw editor + validate/diff/save  
4. **PWA polish** — update prompt (`virtual:pwa-register/vue`), `/health` banner, remove legacy `sw.js` / `manifest.json` / Bootstrap  
5. **Optional** — `@vite-pwa/assets-generator`, virtualize lists, recent searches, i18n EN  

---

## 11. Testing

| Level | Tool |
|-------|------|
| Unit | Vitest |
| Component | Vue Testing Library |
| E2E | Playwright |
| Contract | OpenAPI → types in CI |

---

## 12. Risks

| Risk | Mitigation |
|------|------------|
| shadcn-vue CLI alias on Vite split tsconfig | Set `paths` in both `tsconfig.json` and `tsconfig.app.json` |
| `emptyOutDir` wipes openapi/icons | Emit to `dist`, merge copy; statics in `public/` |
| SPA refresh 404 | .NET fallback → `index.html` |
| GSAP vs CSS motion | GSAP for 2–3 page moments only |
| Dual UI during migration | Feature flag / route cutover per phase |
| Old + new SW both registered | Delete legacy `sw.js` on cutover; one-time `unregister` helper if needed |
| Stale API in Cache | Keep API routes **NetworkOnly** in Workbox |
| Update mid-search | Prefer `registerType: 'prompt'` over blind `autoUpdate` |

---

## 13. Success criteria

- [ ] Vue 3 SPA: Search / Stats / Settings with parity behavior
- [ ] **shadcn-vue** is the only component system (no Bootstrap, no HeroUI)
- [ ] Vite 8 + TypeScript 6+ + Tailwind v4 + GSAP + vue-query + lucide + sonner
- [ ] Embedded in .NET publish/Docker; no Node at runtime
- [ ] PWA via **vite-plugin-pwa** (installable, offline shell, no hand-maintained SW path lists)
- [ ] Typed OpenAPI client
- [ ] Legacy `wwwroot` HTML/JS/`sw.js` removed after cutover

---

## 14. Next steps

1. Scaffold `web/` with **Vue + shadcn-vue** (Tailwind v4).
2. AppShell + theme + API client + API key dialog.
3. Search page → wire Docker/`build.sh`.
4. SPA fallback in `HomeController`; retire old pages phase by phase.
