# JacRed Modern Web UI — Analysis & Rewrite Plan

> Goal: replace the static Bootstrap UI in `wwwroot` with a modern **Node.js + TypeScript** frontend, choosing between **Vue** and **Next.js**, with **shadcn/ui** (or shadcn-vue), while keeping the existing **.NET JacRed API** as the backend.

---

## 1. Current state (`wwwroot`)

### Stack today

| Layer | Technology |
|--------|------------|
| Pages | 3 HTML shells: `index.html`, `stats.html`, `settings.html` |
| CSS | Bootstrap 5.3 + custom `styles.css` (~4.8k lines), Inter fonts, glass / Material-like theme |
| JS | Vanilla IIFE modules (~3.8k lines), no bundler, no TypeScript |
| Icons | Bootstrap Icons (local vendor) |
| Motion | GSAP + ScrollTrigger (local vendor) |
| PWA | `manifest.json`, `sw.js`, offline overlay (`offline-inline.js`) |
| API contract | `openapi.yaml` (served + Swagger) |
| Hosting | ASP.NET `HomeController` returns HTML; `UseStaticFiles` for assets |

### Routes (UI)

| Path | Purpose |
|------|---------|
| `/` | Torrent search (hero, filters, results, TorrServer, API key) |
| `/stats` | Tracker statistics cards / table |
| `/settings` | Config editor (form + raw YAML/JSON, validate, diff, save) |
| `/opensearch.xml` | Browser search plugin (server-generated) |

### Client → API surface used by UI

| Feature | Endpoints |
|---------|-----------|
| Search | `GET /api/v1.0/torrents`, `GET /api/v1.0/conf` |
| Stats | `GET /stats/torrents`, `/stats/meta`, `/stats/tracks` |
| Settings | `GET/POST /api/v1.0/config`, `/schema`, `/validate`, `/diff`, `/render`, `/parse`, `/format` |
| Health / offline | `GET /health` |
| TorrServer | Browser → user-configured TorrServer URL (not JacRed API) |

Auth patterns already in UI:

- **API key** → `localStorage` + query/`X-Api-Key` / Bearer
- **Dev key** → for config API from non-LAN / tunnel
- Same-origin relative URLs (works behind reverse proxy / tunnel)

### Feature inventory to preserve (parity)

**Search (`app.js`)**

- Query by title / KP / IMDB; exact-search toggle
- Server sort: seeds / size / date / update
- Client filters: type, tracker, voice, videotype, year, quality, season, refine, exclude
- URL state sync (`?s=&sort=&…`)
- Infinite “load more” (page size 20)
- Card vs compact list view
- Result actions: open magnet, copy magnet, copy info-hash, send to TorrServer
- Tracker icon badges from `/img/ico/{tracker}.ico`
- Keyboard shortcuts modal, sticky search after results, theme toggle, toasts, a11y (skip link, live regions)

**Stats (`stats.js`)**

- Tracker search + multi-field sort
- Cards / counter, tracks stats optional
- URL sync, API key gate if configured

**Settings (`settings.js` + `settings-form.js`)**

- Schema-driven form + raw YAML/JSON modes
- Unsaved badge, validate, diff preview, format, save
- LAN or `X-Dev-Key` access messaging

**Cross-cutting**

- Light/dark theme (`data-bs-theme` today) + `theme-color` meta
- PWA install + service worker offline shell
- Russian UI copy (`lang="ru"`)

### Pain points of current UI

1. **No build pipeline** — no tree-shaking, typechecking, or component reuse; ~5k-line CSS is hard to evolve.
2. **Duplicated shells** — three nearly identical nav/theme/PWA bootstraps.
3. **String HTML templates** in JS — XSS discipline is manual (`escapeHtml` everywhere).
4. **Bootstrap coupling** — design tokens mixed with utility classes; dark theme tied to Bootstrap attributes.
5. **Hard to test** — no unit/component tests for search filters, config form paths, or SW behavior.
6. **Vendor / CDN ambiguity** — README mentions CDN; repo now vendors Bootstrap/GSAP locally (good for offline, still unbundled).

---

## 2. Stack decision: Vue vs Next.js (+ shadcn)

You asked for **Vue or Next.js** with **shadcn/ui**. They are not interchangeable without picking a UI kit flavor:

| Option | UI kit | Fit for JacRed |
|--------|--------|----------------|
| **A. Next.js (App Router) + React + shadcn/ui** | Official shadcn/ui | Best ecosystem match for “shadcn/ui”; TypeScript-first; can **static-export** into `wwwroot` |
| **B. Vue 3 + Vite + shadcn-vue** | [shadcn-vue](https://www.shadcn-vue.com/) | Closer to “Vue”; same component model as shadcn; lighter SPA, no Node runtime in prod |
| **C. Nuxt 3 + shadcn-vue** | shadcn-vue | SSR optional; usually unnecessary when API is same-host ASP.NET |

### Recommendation: **Option A — Next.js + TypeScript + shadcn/ui** (static export)

**Why**

1. **shadcn/ui is first-class on React/Next** — docs, examples, and community components map 1:1 (Dialog, Tabs, Form, Table, Switch, Toast, Command, Sheet).
2. **JacRed UI is an authenticated SPA over JSON** — Next can run as `output: 'export'` so ASP.NET still serves static files; no second Node process in Docker.
3. **OpenAPI → typed client** — `openapi-typescript` + TanStack Query fits React well.
4. Team already has OpenAPI/Swagger; generating React Query hooks is a clear win for Settings/Search.

**When to choose Vue instead**

- Team preference / existing Vue skills dominate.
- Want a simpler Vite SPA without Next routing/export quirks.
- Then use **Vite + Vue 3 + TypeScript + shadcn-vue + Pinia** with the same folder/API plan below (swap React → Vue components).

**What we are not doing in v1**

- Rewriting the .NET backend to Node.
- Full SSR/SEO for torrent results (private self-hosted tool; SPA/static is enough).
- Replacing Jackett/Torznab/sync APIs — UI only.

---

## 3. Target architecture

```
┌─────────────────────────────────────────────────────────┐
│  Browser (PWA)                                          │
│  Next.js static export  →  / , /stats , /settings       │
│  shadcn/ui + Tailwind + lucide-react                    │
└───────────────────────────┬─────────────────────────────┘
                            │ same-origin fetch
                            ▼
┌─────────────────────────────────────────────────────────┐
│  ASP.NET JacRed (unchanged API)                         │
│  Controllers: torrents, stats, config, health, …        │
│  Serves: wwwroot/out/* (built UI) + API + opensearch    │
└─────────────────────────────────────────────────────────┘
```

### Deployment model

1. New app lives in repo root as `web/` (or `frontend/`).
2. CI / `build.sh` runs `npm ci && npm run build`.
3. Build emits static assets into `wwwroot/` (or `wwwroot/app/` with SPA fallback routes updated in `HomeController`).
4. Docker image still single-process .NET; Node is a **build-time** dependency only.

Preferred Next config:

```ts
// next.config.ts (sketch)
const nextConfig = {
  output: 'export',
  trailingSlash: false,
  images: { unoptimized: true }, // no Next Image optimizer in static+ASP.NET
  basePath: '', // same origin
};
```

`HomeController` keeps `/`, `/stats`, `/settings` but points at exported `index.html` / route HTML (or a single SPA fallback if using client-only routes).

### Alternative (if static export friction)

- **Vite + React + shadcn** SPA: one `index.html`, client router (`/`, `/stats`, `/settings`), ASP.NET fallback all three to `index.html`. Slightly simpler ops; still TypeScript + shadcn.

---

## 4. Proposed project structure

```
web/
  package.json
  tsconfig.json
  next.config.ts
  tailwind.config.ts
  components.json          # shadcn
  public/
    img/                   # migrate from wwwroot/img
    manifest.webmanifest
    sw.js                  # or Serwist / next-pwa strategy
  src/
    app/
      layout.tsx           # nav, theme provider, toasts
      page.tsx             # Search
      stats/page.tsx
      settings/page.tsx
      globals.css
    components/
      layout/AppNav.tsx
      search/SearchForm.tsx
      search/ResultCard.tsx
      search/FiltersSheet.tsx
      stats/TrackerGrid.tsx
      settings/ConfigForm.tsx
      settings/RawEditor.tsx
      settings/DiffDialog.tsx
      ui/                  # shadcn primitives
    lib/
      api/client.ts        # fetch + apiKey/devKey headers
      api/generated/       # openapi-typescript output
      hooks/useTorrents.ts
      hooks/useStats.ts
      hooks/useConfig.ts
      theme.ts
      magnets.ts
      storage.ts
    types/
```

---

## 5. Component / UX mapping (Bootstrap → shadcn)

| Current UI | shadcn / modern equivalent |
|------------|----------------------------|
| Navbar + icon buttons | `NavigationMenu` / custom header + `Button` variant ghost |
| Search bar + sticky dock | Custom layout + `Input` + `Button` |
| Filter panel | `Sheet` (mobile) + `Collapsible` / `Accordion` (desktop) |
| Sort radios | `ToggleGroup` |
| Exact search | `Switch` |
| Result cards | Plain layout (avoid card-for-decoration); `Badge`, `Button` for actions |
| Modals (API key, TorrServer, shortcuts) | `Dialog` |
| Toasts | `sonner` or shadcn `Toaster` |
| Stats controls | `Input`, `Select`, `Table` optional view |
| Settings tabs Form / YAML | `Tabs` |
| Diff preview | `Dialog` + monospace `ScrollArea` |
| Theme toggle | `next-themes` + CSS variables (drop `data-bs-theme`) |
| Icons | `lucide-react` (keep tracker `.ico` assets) |
| GSAP micro-motion | CSS + Framer Motion sparingly (2–3 intentional motions: page enter, results stagger, sticky search) |

Design direction (preserve JacRed identity, modernize):

- Keep dark-first usable theme and brand mark/icon set.
- Move tokens to CSS variables (background, surface, accent, border) — avoid generic purple AI defaults.
- Replace Inter-as-only-stack gradually with a clearer display + UI pair if redesigning; Cyrillic must stay complete.
- Glass panels → subtle surfaces via Tailwind tokens, not Bootstrap “glass-card” copies.

---

## 6. Data layer plan

1. **Generate types** from `wwwroot/openapi.yaml` (or runtime `/swagger/v1/swagger.json`) via `openapi-typescript`.
2. **Thin API client** wrapping `fetch`:
   - attach API key / Dev key from `localStorage`
   - timeouts / `AbortSignal` (keep 15s search, 5s conf)
   - typed errors (401 → open API key dialog)
3. **TanStack Query** for:
   - search (query key: q + filters + sort)
   - stats (+ meta)
   - config schema / document
4. **URL as source of truth** for search/stats filters (`nuqs` or Next `searchParams`).
5. **Local preferences** (theme, list view, TorrServer URL/login, filter panel open) stay in `localStorage` with a small typed store.

---

## 7. PWA & offline

Parity requirements:

- Web App Manifest shortcuts: Search, Stats
- Service worker: precache app shell + fonts/icons; network-first for API; offline fallback page
- `/health` probe for “server down” overlay

Implementation options:

| Approach | Notes |
|----------|--------|
| Keep custom `sw.js` | Lowest risk; adapt cache lists to hashed Next assets |
| Serwist / `@ducanh2912/next-pwa` | Better precache of hashed bundles; needs static-export compatible setup |

Recommendation: **Serwist or next-pwa** in build, with a minimal offline HTML fallback equivalent to today’s `MINIMAL_OFFLINE_HTML`.

---

## 8. Integration with .NET build & Docker

1. Add Node 22 LTS to CI (`build.yml`) and `Dockerfile` **build stage** only.
2. Extend `build.sh` / `Dockerfile`:
   ```bash
   cd web && npm ci && npm run build
   # copy export → wwwroot/
   ```
3. Update `.dockerignore` / `.gitignore` for `web/node_modules`, keep committed `wwwroot` artifacts **or** generate them in CI only (prefer generate-in-CI to avoid dual source of truth).
4. `HomeController` + static file options: ensure SPA routes and `Cache-Control` for HTML remain `no-store`; hashed assets long-cache.
5. Keep serving `openapi.yaml` and Swagger unchanged.

---

## 9. Phased delivery

### Phase 0 — Foundations (no user-facing cutover)

- Scaffold `web/` (Next + TS + Tailwind + shadcn).
- Theme tokens, App shell (nav, theme, toast).
- OpenAPI codegen + API client + key storage dialogs.
- Wire static export into Docker/CI; still ship old HTML until Phase 1.

### Phase 1 — Search parity

- Search page: form, filters, sort, results, load more, URL sync.
- Magnet / hash / TorrServer actions.
- Keyboard shortcuts, empty/help/not-found states.
- Feature flag or path swap: `/` serves new UI when build flag set.

### Phase 2 — Stats parity

- Tracker grid, sort/search, meta timestamps, tracks block.
- Share nav/API key with Phase 1.

### Phase 3 — Settings parity

- Schema form renderer (port `settings-form.js` logic to typed components).
- Raw editor (CodeMirror or Monaco — lazy-loaded).
- Validate / diff / format / save + access-denied UX for Dev key.

### Phase 4 — PWA, polish, remove legacy

- SW + manifest + OpenSearch still via .NET.
- A11y pass, visual polish, motion.
- Delete old `wwwroot/*.html` + `js/*` + Bootstrap vendor once confidence is high.
- Shrink CSS surface to Tailwind + a small `globals.css`.

### Phase 5 (optional) — Enhancements beyond parity

- Virtualized long result lists
- Saved searches / recent queries
- Better settings search within schema
- i18n scaffolding (RU default, EN optional)
- Playwright e2e against TestServer / Docker

---

## 10. Testing strategy

| Level | Tools | What |
|-------|-------|------|
| Unit | Vitest | magnet hash parse, filter logic, config path get/set |
| Component | Testing Library | SearchForm, ResultCard actions, ConfigForm field types |
| E2E | Playwright | search happy path, API key modal, settings validate/diff (mocked API) |
| Contract | openapi diff in CI | fail if UI client types drift from `openapi.yaml` |

Keep existing .NET tests for API; frontend tests are additive.

---

## 11. Risks & mitigations

| Risk | Mitigation |
|------|------------|
| Static export + dynamic routes | Prefer static pages; client-side search params only |
| Hashed assets break SW | Generate precache manifest in build |
| Settings form is complex | Port schema-driven renderer first; keep raw YAML path as escape hatch |
| TorrServer CORS | Document same as today (user URL; browser CORS constraints unchanged) |
| Dual UI during migration | Feature flag / single cutover after Phase 3 |
| Bundle size vs today’s tiny scripts | Code-split settings editor; analyze with `@next/bundle-analyzer` |
| Design regression | Side-by-side checklist from §1 feature inventory |

---

## 12. Success criteria

- [ ] Feature parity on Search, Stats, Settings (checklist in §1)
- [ ] TypeScript strict mode; API calls typed from OpenAPI
- [ ] Single design system (shadcn + tokens); no Bootstrap dependency
- [ ] PWA installable; offline shell works
- [ ] Docker image remains one runtime (ASP.NET); Node only at build
- [ ] Lighthouse a11y ≥ current; no major keyboard/regressions
- [ ] Legacy `wwwroot` HTML/JS/Bootstrap removed after cutover

---

## 13. Immediate next steps (implementation kickoff)

1. Confirm stack choice: **Next.js + shadcn/ui** (recommended) vs **Vue + shadcn-vue**.
2. Scaffold `web/` and shadcn init (Button, Input, Dialog, Tabs, Switch, Badge, Sheet, Sonner).
3. Implement API client + OpenAPI types.
4. Build Search page to parity behind a build flag.
5. Hook `Dockerfile` / CI to produce static export into `wwwroot`.

---

## Appendix A — LOC snapshot (baseline)

| Area | Approx. size |
|------|----------------|
| `wwwroot/js/*.js` | ~3.8k lines |
| `wwwroot/css/styles.css` | ~4.8k lines |
| HTML shells | ~840 lines |
| OpenAPI | ~1.4k lines |
| UI pages | 3 |

## Appendix B — Vue variant (if selected)

Same phases and API plan; differences only:

- `web/` = Vite + Vue 3 + Vue Router + Pinia + shadcn-vue + Tailwind
- TanStack Query → `@tanstack/vue-query`
- `next-themes` → `@vueuse/core` color mode
- Static build to `wwwroot/` via `vite build --outDir ../wwwroot`

Architecture with ASP.NET backend stays identical.
