import { beforeEach, describe, expect, it } from 'vitest'
import {
  clearRecentSearches,
  getRecentSearches,
  pushRecentSearch,
} from '@/lib/recent-searches'

const memory = new Map<string, string>()

beforeEach(() => {
  memory.clear()
  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    value: {
      getItem: (key: string) => memory.get(key) ?? null,
      setItem: (key: string, value: string) => memory.set(key, value),
      removeItem: (key: string) => memory.delete(key),
    },
  })
  clearRecentSearches()
})

describe('recent searches', () => {
  it('deduplicates case-insensitively and keeps newest first', () => {
    pushRecentSearch('Movie')
    pushRecentSearch('Series')
    pushRecentSearch(' movie ')
    expect(getRecentSearches()).toEqual(['movie', 'Series'])
  })

  it('limits the history size', () => {
    for (let index = 0; index < 12; index += 1) {
      pushRecentSearch(`query-${index}`)
    }
    expect(getRecentSearches()).toHaveLength(8)
  })
})
