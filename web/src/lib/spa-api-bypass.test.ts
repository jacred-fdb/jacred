import { describe, expect, it } from 'vitest'
import {
  isApiPathname,
  NAVIGATE_FALLBACK_DENYLIST,
  WORKER_FIRST_PATTERNS,
} from '@/lib/spa-api-bypass'

describe('isApiPathname', () => {
  it('treats system JSON endpoints as API', () => {
    expect(isApiPathname('/health')).toBe(true)
    expect(isApiPathname('/version')).toBe(true)
    expect(isApiPathname('/lastupdatedb')).toBe(true)
  })

  it('keeps SPA shells off the API list', () => {
    expect(isApiPathname('/')).toBe(false)
    expect(isApiPathname('/stats')).toBe(false)
    expect(isApiPathname('/stats/')).toBe(false)
    expect(isApiPathname('/settings')).toBe(false)
  })

  it('treats /stats/<action> as API (not the SPA page)', () => {
    expect(isApiPathname('/stats/meta')).toBe(true)
    expect(isApiPathname('/stats/torrents')).toBe(true)
    expect(isApiPathname('/stats/tracks')).toBe(true)
  })

  it('covers search / sync / admin prefixes', () => {
    expect(isApiPathname('/api/v1.0/torrents')).toBe(true)
    expect(isApiPathname('/api/v1.0/config')).toBe(true)
    expect(isApiPathname('/sync/fdb')).toBe(true)
    expect(isApiPathname('/torznab/api')).toBe(true)
    expect(isApiPathname('/cron/anything')).toBe(true)
    expect(isApiPathname('/dev/updateSize')).toBe(true)
    expect(isApiPathname('/jsondb/save')).toBe(true)
    expect(isApiPathname('/swagger/index.html')).toBe(true)
  })
})

describe('NAVIGATE_FALLBACK_DENYLIST', () => {
  function denied(pathname: string) {
    return NAVIGATE_FALLBACK_DENYLIST.some((re) => re.test(pathname))
  }

  it('denies JSON under /stats but not the SPA /stats page', () => {
    expect(denied('/stats')).toBe(false)
    expect(denied('/stats/meta')).toBe(true)
    expect(denied('/stats/torrents')).toBe(true)
    expect(denied('/stats/tracks')).toBe(true)
  })

  it('denies system and API navigations', () => {
    expect(denied('/version')).toBe(true)
    expect(denied('/health')).toBe(true)
    expect(denied('/api/v2.0/indexers')).toBe(true)
    expect(denied('/sync/conf')).toBe(true)
  })
})

describe('WORKER_FIRST_PATTERNS', () => {
  it('includes stats JSON and system endpoints', () => {
    expect(WORKER_FIRST_PATTERNS).toContain('/stats/meta')
    expect(WORKER_FIRST_PATTERNS).toContain('/stats/torrents')
    expect(WORKER_FIRST_PATTERNS).toContain('/stats/tracks')
    expect(WORKER_FIRST_PATTERNS).toContain('/version')
    expect(WORKER_FIRST_PATTERNS).toContain('/health')
    expect(WORKER_FIRST_PATTERNS).toContain('/api/*')
  })

  it('does not claim the SPA /stats shell', () => {
    expect(WORKER_FIRST_PATTERNS).not.toContain('/stats')
    expect(WORKER_FIRST_PATTERNS).not.toContain('/stats/*')
  })
})
