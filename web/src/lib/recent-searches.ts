import { getItem, setItem, StorageKeys } from '@/lib/storage'

const MAX_RECENT = 8

export function getRecentSearches(): string[] {
  try {
    const raw = getItem(StorageKeys.recentSearches)
    if (!raw) return []
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return []
    return parsed
      .filter((x): x is string => typeof x === 'string' && x.trim().length > 0)
      .map((x) => x.trim())
      .slice(0, MAX_RECENT)
  } catch {
    return []
  }
}

export function pushRecentSearch(query: string): string[] {
  const q = query.trim()
  if (!q) return getRecentSearches()
  const next = [q, ...getRecentSearches().filter((x) => x.toLowerCase() !== q.toLowerCase())].slice(
    0,
    MAX_RECENT,
  )
  setItem(StorageKeys.recentSearches, JSON.stringify(next))
  return next
}

export function clearRecentSearches(): void {
  setItem(StorageKeys.recentSearches, '[]')
}
