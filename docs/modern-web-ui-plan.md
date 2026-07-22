# JacRed Modern Web UI — Rewrite Plan

> Replace the Bootstrap/vanilla `wwwroot` UI with a **fully new** SPA using modern tooling. Keep the **.NET JacRed API** as the backend.

**Chosen stack**

| Layer | Choice |
|--------|--------|
| UI runtime | **React 19** |
| Bundler | **Vite 8** (Rolldown) |
| Language | **TypeScript 6** (or 7.x if you want the Go-native compiler immediately) |
| Components | **HeroUI v3** (`@heroui/react` + `@heroui/styles`) |
| CSS | **Tailwind CSS v4** |
| Motion | **GSAP** (+ ScrollTrigger where useful) |
| Server state | **TanStack Query** |
| Icons | **lucide-react** |
| Toasts | **sonner** |
| Routing | **React Router 7** (SPA) |
| API types | **openapi-typescript** from `openapi.yaml` |

**Hosting decision (recommended): embed built assets in the .NET artifact** — see [§3](#3-hosting-embed-in-dotnet-vs-separate).

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

**Parity features to keep (product behavior, not UI look):**

- Search: query, exact mode, sort, filters, URL sync, load-more, card/list, magnet/hash/TorrServer, API key
- Stats: tracker grid, sort/search, meta/tracks
- Settings: schema form + raw YAML/JSON, validate/diff/format/save, Dev key / LAN access
- Theme, PWA offline shell, Russian UI, OpenSearch (`/opensearch.xml` stays on .NET)

This is a **greenfield UI**, not a Bootstrap restyle. Visual language, tokens, and components are new (HeroUI + JacRed brand).

---

## 2. Why this stack (and not Next / shadcn)

| Decision | Rationale |
|----------|-----------|
| **Vite SPA vs Next.js** | JacRed is same-origin over a .NET API. No need for SSR/RSC/Node in production. Vite 8 + static `dist/` fits Docker single-binary better. |
| **HeroUI v3 vs shadcn** | HeroUI v3 is built for React 19 + Tailwind v4, React Aria a11y, compound components, CSS-variable theming — strong match for a polished admin/search UI. |
| **GSAP kept** | Already part of JacRed’s feel (results rise, sticky search). HeroUI handles micro-interactions in CSS; GSAP for intentional page/result choreography only (2–3 motions). |
| **TanStack Query** | Ideal for search/stats/config: cache, abort, retries, 401 → API-key modal. |
| **sonner + lucide** | Lightweight, modern defaults; replace Bootstrap toasts/icons. |

**Note on TypeScript:** npm currently publishes **7.x** as latest. Pin **TypeScript 6** if you want the last JS-based compiler line; move to **7** when ready for the native compiler. Either works with Vite 8 / React 19.

---

## 3. Hosting: embed in .NET vs separate

### Option A — Embed in .NET build artifact (**recommended**)

```
CI / Docker build:
  Node builds web/ → dist/
  copy dist/ → wwwroot/ (or publish output)
  dotnet publish → single artifact / Docker image
Runtime:
  one process (ASP.NET) serves API + UI on one port (e.g. 9117)
```

| Pros | Cons |
|------|------|
| Same UX as today: one install, one port, one Docker image | Node required at **build** time (not runtime) |
| No CORS; API key / cookies / tunnel just work | Frontend release tied to app release (or rebuild image) |
| `jacred.sh`, systemd, reverse proxy unchanged | Slightly larger image (hashed JS/CSS) |
| Offline/PWA same-origin simple | — |

**This matches how JacRed is distributed today** and is the default for self-hosted aggregators (Jackett-style).

### Option B — Separate frontend deploy

```
web/ → own host (nginx, Cloudflare Pages, etc.)
API → JacRed :9117
Browser calls API via VITE_API_BASE + CORS
```

| Pros | Cons |
|------|------|
| Independent UI deploys | CORS, cookies, tunnel, apikey more fragile |
| CDN for static assets | Two things to install/upgrade for users |
| — | Breaks “one binary / one Docker” story |
| — | PWA/offline and OpenSearch harder |

**Use Option B only if** you later want a public marketing site or multi-tenant cloud UI. For core JacRed: **Option A**.

### Recommended hybrid workflow

- Source of truth: `web/` in this repo (monorepo).
- **Do not** hand-edit built files in `wwwroot`.
- CI/Docker: `npm ci && npm run build` → emit into publish `wwwroot`.
- Optionally keep committed `wwwroot` **empty of app JS** (only `openapi.yaml`, favicons during transition) so there is no dual source of truth.
- Dev: Vite `server.proxy` → local JacRed (`localhost:9117`).

```ts
// vite.config.ts (sketch)
export default defineConfig({
  plugins: [react(), /* PWA plugin */],
  build: { outDir: '../wwwroot', emptyOutDir: true /* careful with openapi/img during migration */ },
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
});
```

During migration, prefer `outDir: 'dist'` and a copy step that **merges** into `wwwroot` (preserve `openapi.yaml`, tracker icons) until cutover is complete.

---

## 4. Target architecture

```
┌──────────────────────────────────────────────┐
│  Browser PWA                                 │
│  React 19 + HeroUI v3 + TanStack Query       │
│  Routes: /  /stats  /settings                │
└──────────────────┬───────────────────────────┘
                   │ same-origin fetch
                   ▼
┌──────────────────────────────────────────────┐
│  ASP.NET JacRed (unchanged)                  │
│  API + Torznab + Sync + OpenSearch           │
│  Serves static SPA from wwwroot              │
└──────────────────────────────────────────────┘
```

**.NET changes (small):**

1. SPA fallback: `/`, `/stats`, `/settings` → `wwwroot/index.html` (or Vite multi-page if preferred — SPA + React Router is simpler).
2. Long-cache hashed assets (`/assets/*`); HTML `no-store` (as today).
3. Dockerfile **build** stage: install Node 22 LTS, build `web/`, copy into publish output.
4. Runtime stage: **no Node**.

---

## 5. Project structure

```
web/
  package.json
  vite.config.ts
  tsconfig.json
  index.html
  public/
    img/                 # from wwwroot/img (brand + tracker icos)
    manifest.webmanifest
  src/
    main.tsx
    app/App.tsx
    app/router.tsx
    styles/
      globals.css        # @import "tailwindcss"; @import "@heroui/styles";
      theme.css          # JacRed tokens (oklch) overriding HeroUI vars
    pages/
      SearchPage.tsx
      StatsPage.tsx
      SettingsPage.tsx
    components/
      layout/AppShell.tsx
      search/...
      stats/...
      settings/...
    lib/
      api/client.ts
      api/types.ts       # generated
      magnets.ts
      storage.ts
    hooks/
      useTorrents.ts
      useStats.ts
      useConfig.ts
      useTheme.ts
    motion/
      gsap.ts            # register plugins; reduced-motion guard
```

---

## 6. UI / UX direction (fully new)

- **One app shell**: shared nav (Search / Stats / Settings), theme toggle, API key / Dev key entry.
- **HeroUI compounds**: Button, Input, Select, Switch, Tabs, Modal, Drawer/Sheet, Badge, Table (optional stats), Spinner, Tooltip.
- **Search first viewport**: brand + one search field + short subtitle + primary CTA — no stats clutter in the hero.
- **Results**: avoid decorative cards; use clear list/rows with action buttons; optional denser list mode.
- **Theme**: CSS variables / OKLCH via HeroUI; light + dark; drop Bootstrap `data-bs-theme`.
- **Motion (GSAP)**: (1) first visit hero settle, (2) results stagger on search, (3) sticky search dock transition — respect `prefers-reduced-motion`.
- **Copy**: keep Russian as default; structure strings for later i18n.

---

## 7. Data layer

1. Generate types from `wwwroot/openapi.yaml` → `src/lib/api/types.ts`.
2. `apiClient` with:
   - API key / Dev key headers (parity with `common.js`)
   - timeouts (15s search, 5s conf)
   - typed error helpers (401)
3. TanStack Query:
   - `useTorrentsQuery(search, filters, sort)`
   - `useStatsQuery` / `useStatsMetaQuery`
   - `useConfigQuery` + mutations (validate, diff, save, format)
4. URL state (`?s=&sort=&…`) via React Router search params.
5. Local prefs: theme, list view, TorrServer URL/creds, filter panel open → `localStorage` typed helpers.

---

## 8. PWA

- Vite PWA plugin (e.g. `vite-plugin-pwa` / Workbox) generating SW with hashed precache.
- Manifest shortcuts: Search, Stats.
- Network-first for API; cache-first for static; offline fallback page.
- Keep `/health` probe for “server unreachable” banner (port of `offline-inline.js`).

---

## 9. Build & Docker integration (Option A)

**Dockerfile build stage additions (sketch):**

```dockerfile
# after COPY . .
RUN apk add --no-cache nodejs npm
WORKDIR /src/web
RUN npm ci && npm run build
WORKDIR /src
# ensure publish includes wwwroot with SPA assets
```

**`build.sh`:** run `web` build before `dotnet publish` when Node is available; fail CI if web build fails.

**`.csproj`:** optional MSBuild target `NpmBuild` BeforeTargets Publish for local `dotnet publish`.

**Git:**

- Commit `web/` source.
- Ignore `web/node_modules`, `web/dist`.
- Built `wwwroot` assets: prefer **CI-produced** (cleaner) over committing bundles.

---

## 10. Phased delivery

### Phase 0 — Scaffold & pipeline

- Create `web/` with React 19 + Vite 8 + TS + Tailwind v4 + HeroUI v3.
- Theme tokens, AppShell, sonner, lucide, QueryClientProvider.
- OpenAPI codegen + API client + API key modal.
- Vite proxy for local .NET.
- Wire Docker/CI to build and copy into artifact (feature flag: still serve old HTML until Phase 1).

### Phase 1 — Search (new UI)

- Full search parity; GSAP result motion; TorrServer dialog.
- Cut `/` over to SPA.

### Phase 2 — Stats

- Tracker grid + controls; share shell/auth.

### Phase 3 — Settings

- Schema form + raw editor (CodeMirror/Monaco lazy).
- Validate / diff / save + access-denied UX.

### Phase 4 — PWA + remove legacy

- SW/manifest polish.
- Delete Bootstrap, old HTML/JS, vendor duplicates.
- A11y + Playwright smoke tests.

### Phase 5 — Optional

- Virtualized results, recent searches, i18n EN, React Compiler opt-in via Vite 8 babel preset.

---

## 11. Testing

| Level | Tool | Focus |
|-------|------|--------|
| Unit | Vitest | magnets, filters, storage, config path helpers |
| Component | Testing Library | SearchForm, ResultRow actions, ConfigForm |
| E2E | Playwright | search, API key, settings validate (mock API) |
| Contract | CI | `openapi-typescript` regenerates cleanly |

---

## 12. Risks

| Risk | Mitigation |
|------|------------|
| `emptyOutDir` wipes `openapi.yaml` / icons | Merge copy step; put statics in `web/public` |
| SPA deep links 404 on refresh | .NET fallback all UI routes → `index.html` |
| HeroUI v3 API learning curve | Stick to compound patterns; no v2 Provider |
| GSAP + HeroUI motion overlap | GSAP only for page-level; leave control animations to CSS |
| Image size growth | Code-split settings editor; analyze rollup output |
| TS 6 vs 7 | Start on 6 as specified; bump to 7 in a follow-up |

---

## 13. Success criteria

- [ ] New SPA for Search / Stats / Settings with parity behavior
- [ ] Stack: React 19, Vite 8, TypeScript 6+, HeroUI v3, Tailwind v4, GSAP, TanStack Query, lucide, sonner
- [ ] **Embedded** in .NET publish/Docker; no Node at runtime
- [ ] No Bootstrap / legacy wwwroot JS after cutover
- [ ] PWA installable; same-origin API
- [ ] Typed API client from OpenAPI

---

## 14. Immediate next steps

1. Approve **Option A (embed in .NET artifact)** vs separate (default: A).
2. Scaffold `web/` with the stack above.
3. Implement AppShell + API client + Search page.
4. Hook Dockerfile / `build.sh` / CI.
5. Flip `HomeController` to SPA fallback and retire old HTML page-by-page.

---

## Appendix — npm versions observed (plan date)

| Package | Latest seen |
|---------|-------------|
| `react` | 19.x |
| `vite` | 8.x |
| `typescript` | 7.x latest (6.x available to pin) |
| `@heroui/react` | 3.x |
| `gsap` | 3.x |
| `@tanstack/react-query` | 5.x |
