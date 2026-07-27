import { describe, expect, it } from 'vitest'
import en from '@/i18n/en'
import ru from '@/i18n/ru'

function keyPaths(value: unknown, prefix = ''): string[] {
  if (!value || typeof value !== 'object') return [prefix]
  return Object.entries(value)
    .flatMap(([key, child]) => keyPaths(child, prefix ? `${prefix}.${key}` : key))
    .sort()
}

describe('translations', () => {
  it('keeps RU and EN key coverage identical', () => {
    expect(keyPaths(en)).toEqual(keyPaths(ru))
  })

  it('keeps search API mode labels aligned with EN', () => {
    expect(ru.search.apiMode.native).toBe(en.search.apiMode.native)
    expect(ru.search.apiMode.jackett).toBe(en.search.apiMode.jackett)
  })
})
