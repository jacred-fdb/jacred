const MAX_MAGNET_LENGTH = 8_192

/** Rejects non-magnet schemes and oversized payloads before copy / TorrServer send. */
export function isSafeMagnetUrl(value: string | null | undefined): boolean {
  if (!value || typeof value !== 'string') return false
  const magnet = value.trim()
  if (!magnet || magnet.length > MAX_MAGNET_LENGTH) return false
  return /^magnet:\?/i.test(magnet)
}

/** Extracts a lowercase info-hash from a magnet URI, or `''` if invalid. */
export function extractInfoHash(magnet: string | null | undefined): string {
  if (!isSafeMagnetUrl(magnet)) return ''
  const m = (magnet as string).match(
    /urn:btih:([a-fA-F0-9]{40}|[a-zA-Z2-7]{32}|[a-fA-F0-9]{64})/i,
  )
  return m ? m[1].toLowerCase() : ''
}

/** Clipboard write with a `document.execCommand` fallback for older WebViews. */
export async function copyText(text: string): Promise<void> {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text)
    return
  }
  const ta = document.createElement('textarea')
  ta.value = text
  ta.style.cssText = 'position:fixed;opacity:0;top:0;left:0'
  document.body.appendChild(ta)
  ta.focus()
  ta.select()
  try {
    const ok = document.execCommand('copy')
    if (!ok) throw new Error('copy failed')
  } finally {
    document.body.removeChild(ta)
  }
}

export type TorrServerCredentials = {
  baseUrl: string
  login?: string
  password?: string
}

export type TorrServerErrorCode =
  | 'invalidMagnet'
  | 'missingUrl'
  | 'unauthorized'
  | 'cors'
  | 'request'

export class TorrServerError extends Error {
  readonly code: TorrServerErrorCode
  readonly status?: number

  constructor(
    code: TorrServerErrorCode,
    status?: number,
  ) {
    super(code)
    this.name = 'TorrServerError'
    this.code = code
    this.status = status
  }
}

/** POST magnet to a TorrServer `/torrents` endpoint (Basic auth supported via URL or creds). */
export async function sendToTorrServer(
  magnet: string,
  creds: TorrServerCredentials,
): Promise<void> {
  if (!isSafeMagnetUrl(magnet)) throw new TorrServerError('invalidMagnet')
  const baseUrl = creds.baseUrl.trim()
  if (!baseUrl) throw new TorrServerError('missingUrl')

  let torUrl = `${baseUrl.replace(/\/$/, '')}/torrents`
  let authHeader: string | null = null

  try {
    const urlObj = new URL(baseUrl)
    if (urlObj.username || urlObj.password) {
      authHeader = `Basic ${btoa(
        `${decodeURIComponent(urlObj.username || '')}:${decodeURIComponent(urlObj.password || '')}`,
      )}`
      const path =
        urlObj.pathname === '/' ? '' : urlObj.pathname.replace(/\/$/, '')
      torUrl = `${urlObj.origin.replace(/\/$/, '')}${path}/torrents`
    }
  } catch {
    /* keep torUrl as-is */
  }

  if (!authHeader && creds.login && creds.password) {
    authHeader = `Basic ${btoa(`${creds.login}:${creds.password}`)}`
  }

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (authHeader) headers.Authorization = authHeader

  const res = await fetch(torUrl, {
    method: 'POST',
    headers,
    body: JSON.stringify({
      action: 'add',
      link: magnet,
      save_to_db: true,
    }),
  })

  if (res.ok) return

  if (res.status === 401) {
    throw new TorrServerError('unauthorized', res.status)
  }
  if (res.status === 403) {
    throw new TorrServerError('cors', res.status)
  }
  throw new TorrServerError('request', res.status)
}
