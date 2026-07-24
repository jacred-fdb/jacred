/**
 * Reverse-proxy JacRed API paths to JACRED_ORIGIN.
 * Static SPA assets are served by Workers Assets (see wrangler.jsonc).
 */

export interface Env {
  JACRED_ORIGIN?: string
  ASSETS: Fetcher
}

/** Browser / auth headers to copy from the incoming request. */
const FORWARD_REQUEST_HEADERS = new Set([
  'accept',
  'accept-language',
  'authorization',
  'content-type',
  'cookie',
  'if-none-match',
  'if-modified-since',
  'origin',
  'referer',
  'user-agent',
  'x-api-key',
  'x-dev-key',
])

const HOP_BY_HOP_RESPONSE_HEADERS = new Set([
  'connection',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'te',
  'trailers',
  'transfer-encoding',
  'upgrade',
])

const CLIENT_NAME = 'jacred-web'

function parseOrigin(raw: string | undefined): URL | null {
  if (!raw?.trim()) return null
  try {
    const url = new URL(raw.trim())
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return null
    return url
  } catch {
    return null
  }
}

function buildUpstreamUrl(origin: URL, requestUrl: URL): URL {
  return new URL(requestUrl.pathname + requestUrl.search, origin)
}

/** Visitor IP as seen by Cloudflare (or first XFF hop if present). */
function resolveClientIp(request: Request): string | null {
  const cfIp = request.headers.get('CF-Connecting-IP')?.trim()
  if (cfIp) return cfIp
  const xff = request.headers.get('X-Forwarded-For')?.split(',')[0]?.trim()
  return xff || null
}

function forwardRequestHeaders(request: Request, upstreamHost: string): Headers {
  const out = new Headers()
  for (const [name, value] of request.headers) {
    if (FORWARD_REQUEST_HEADERS.has(name.toLowerCase())) {
      out.set(name, value)
    }
  }

  out.set('Host', upstreamHost)

  // Identify this proxy to JacRed / logs.
  out.set('X-JacRed-Client', CLIENT_NAME)
  out.set('Via', `1.1 ${CLIENT_NAME}`)

  const requestUrl = new URL(request.url)
  out.set('X-Forwarded-Proto', requestUrl.protocol.replace(':', ''))
  out.set('X-Forwarded-Host', requestUrl.host)

  const clientIp = resolveClientIp(request)
  if (clientIp) {
    // JacRed ClientNetworkContext prefers CF-Connecting-IP, then X-Real-IP.
    out.set('CF-Connecting-IP', clientIp)
    out.set('X-Real-IP', clientIp)
    const prior = request.headers.get('X-Forwarded-For')?.trim()
    out.set('X-Forwarded-For', prior || clientIp)
  }

  const cfRay = request.headers.get('CF-Ray')
  if (cfRay) out.set('CF-Ray', cfRay)

  return out
}

function sanitizeResponseHeaders(headers: Headers): Headers {
  const out = new Headers(headers)
  for (const name of HOP_BY_HOP_RESPONSE_HEADERS) {
    out.delete(name)
  }
  // Never cache proxied API responses at the edge or browser by default.
  out.set('Cache-Control', 'no-store')
  return out
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const origin = parseOrigin(env.JACRED_ORIGIN)
    if (!origin) {
      return new Response(
        'JACRED_ORIGIN is not set or invalid. Set an absolute http(s) origin (e.g. https://jacred.example.com).',
        { status: 500, headers: { 'Content-Type': 'text/plain; charset=utf-8' } },
      )
    }

    const requestUrl = new URL(request.url)
    const upstreamUrl = buildUpstreamUrl(origin, requestUrl)
    const init: RequestInit & { duplex?: 'half' } = {
      method: request.method,
      headers: forwardRequestHeaders(request, origin.host),
      redirect: 'manual',
    }

    if (request.method !== 'GET' && request.method !== 'HEAD') {
      init.body = request.body
      init.duplex = 'half'
    }

    const upstream = await fetch(upstreamUrl, init)
    return new Response(upstream.body, {
      status: upstream.status,
      statusText: upstream.statusText,
      headers: sanitizeResponseHeaders(upstream.headers),
    })
  },
}
