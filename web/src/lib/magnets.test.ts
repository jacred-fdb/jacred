import { describe, expect, it } from 'vitest'
import { extractInfoHash, isSafeMagnetUrl } from '@/lib/magnets'

describe('magnet helpers', () => {
  it('accepts only magnet query URLs', () => {
    expect(isSafeMagnetUrl('magnet:?xt=urn:btih:abc')).toBe(true)
    expect(isSafeMagnetUrl(' MAGNET:?xt=urn:btih:abc ')).toBe(true)
    expect(isSafeMagnetUrl('javascript:alert(1)')).toBe(false)
    expect(isSafeMagnetUrl('https://example.com/file.torrent')).toBe(false)
  })

  it('extracts BTIH only from safe magnet URLs', () => {
    const hash = '0123456789abcdef0123456789abcdef01234567'
    expect(extractInfoHash(`magnet:?xt=urn:btih:${hash.toUpperCase()}`)).toBe(
      hash,
    )
    expect(extractInfoHash(`https://example.test/urn:btih:${hash}`)).toBe('')
  })
})
