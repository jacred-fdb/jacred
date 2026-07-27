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

/** True when pathname is an API/backend route (not SPA / static asset). */
export function isApiPathname(pathname: string): boolean {
  const p = pathname.split('?')[0] || ''
  if (p === '/health' || p === '/version' || p === '/lastupdatedb') return true
  if (p === '/opensearch.xml' || p === '/openapi.yaml') return true
  if (p === '/jsondb' || p.startsWith('/jsondb/')) return true
  if (p === '/torznab' || p.startsWith('/torznab/')) return true
  if (p.startsWith('/api/')) return true
  if (p.startsWith('/swagger')) return true
  if (p.startsWith('/sync/')) return true
  if (p.startsWith('/cron/')) return true
  if (p.startsWith('/dev/')) return true
  // /stats → SPA; /stats/meta|torrents|tracks|… → API
  if (/^\/stats\/.+/i.test(p)) return true
  return false
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
