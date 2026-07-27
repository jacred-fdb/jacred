/**
 * Paths that must NEVER hit the Vue SPA shell (index.html).
 * Keep in sync with:
 * - wrangler.jsonc → assets.run_worker_first
 * - Controllers/* Route attributes / JacRedAccessCatalog
 *
 * SPA shells (assets / HomeController): `/`, `/stats`, `/settings` only.
 * JSON under `/stats/*` (torrents|meta|tracks) is API — not the SPA page.
 */

/** Cloudflare Workers `run_worker_first` patterns (exact + glob). */
export const WORKER_FIRST_PATTERNS = [
  '/api/*',
  '/stats/torrents',
  '/stats/meta',
  '/stats/tracks',
  '/health',
  '/version',
  '/lastupdatedb',
  '/opensearch.xml',
  '/swagger',
  '/swagger/*',
  '/sync/*',
  '/torznab',
  '/torznab/*',
  '/cron/*',
  '/dev/*',
  '/jsondb',
  '/jsondb/*',
] as const

/** Workbox navigateFallbackDenylist — full navigations that must stay Network/API. */
export const NAVIGATE_FALLBACK_DENYLIST: RegExp[] = [
  /^\/api\//,
  /^\/swagger(?:\/|$)/,
  /^\/torznab(?:\/|$)/,
  /^\/sync\//,
  /^\/cron\//,
  /^\/dev\//,
  /^\/jsondb(?:\/|$)/,
  // SPA is exactly /stats; JSON lives under /stats/<action>
  /^\/stats\/.+/,
  /^\/health(?:\/|$)/,
  /^\/version(?:\/|$)/,
  /^\/lastupdatedb(?:\/|$)/,
  /^\/opensearch\.xml$/,
  /^\/openapi\.yaml$/,
]

/**
 * Workbox `urlPattern` matcher for API/backend routes.
 *
 * Must stay closure-free: `generateSW` embeds this via `.toString()` into `sw.js`
 * and cannot resolve module imports from the Vite config.
 */
export function matchApiUrlPattern({ url }: { url: { pathname: string } }): boolean {
  // Regex is inlined (not imported) so Workbox can serialize this function into the SW.
  return /^\/(?:api\/|swagger|sync\/|cron\/|dev\/|jsondb(?:\/|$)|torznab(?:\/|$)|stats\/.+|health(?:\/|$)|version(?:\/|$)|lastupdatedb(?:\/|$)|opensearch\.xml$|openapi\.yaml$)/i.test(
    url.pathname,
  )
}

/** True when pathname is an API/backend route (not SPA / static asset). */
export function isApiPathname(pathname: string): boolean {
  const p = pathname.split('?')[0] || ''
  return matchApiUrlPattern({ url: { pathname: p } })
}

/** Vite `server.proxy` keys → upstream JacRed. */
export const VITE_API_PROXY_PATHS = [
  '/api',
  '/stats/torrents',
  '/stats/meta',
  '/stats/tracks',
  '/health',
  '/version',
  '/lastupdatedb',
  '/opensearch.xml',
  '/sync',
  '/torznab',
  '/cron',
  '/dev',
  '/jsondb',
  '/swagger',
] as const
