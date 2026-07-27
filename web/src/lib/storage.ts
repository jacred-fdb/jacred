/** localStorage keys — same names as legacy wwwroot UI for upgrade continuity */
export const StorageKeys = {
  apiKey: 'api_key',
  devKey: 'dev_key',
  theme: 'theme',
  surface: 'jacredSurface',
  listView: 'jacredListView',
  filtersOpen: 'jacredFiltersOpen',
  apiMode: 'jacredApiMode',
  torrServerUrl: 'jacredTorServerUrl',
  torrServerLogin: 'jacredTorServerLogin',
  torrServerPassword: 'jacredTorServerPassword',
  search: 'search',
  sort: 'sort',
  exact: 'exact',
  settingsFormTab: 'jacredSettingsFormTab',
  settingsMode: 'jacredSettingsMode',
  legacySwCleanupDone: 'jacredLegacySwCleanupDone',
  locale: 'jacredLocale',
  recentSearches: 'jacredRecentSearches',
} as const

export type StorageKey = (typeof StorageKeys)[keyof typeof StorageKeys]

export function getItem(key: StorageKey): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

export function setItem(key: StorageKey, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    /* private mode / quota */
  }
}

export function removeItem(key: StorageKey): void {
  try {
    localStorage.removeItem(key)
  } catch {
    /* ignore */
  }
}

export type ThemeValue = 'light' | 'dark'

export function getTheme(): ThemeValue {
  const raw = getItem(StorageKeys.theme)
  return raw === 'light' ? 'light' : 'dark'
}

export function setTheme(theme: ThemeValue): void {
  setItem(StorageKeys.theme, theme)
}

export type SurfaceValue = 'solid' | 'glass'

export function getSurface(): SurfaceValue {
  const raw = getItem(StorageKeys.surface)
  return raw === 'glass' ? 'glass' : 'solid'
}

export function setSurface(surface: SurfaceValue): void {
  setItem(StorageKeys.surface, surface)
}

export function getApiKey(): string {
  return getItem(StorageKeys.apiKey) ?? ''
}

export function setApiKey(value: string): void {
  if (value) setItem(StorageKeys.apiKey, value)
  else removeItem(StorageKeys.apiKey)
}

export function getDevKey(): string {
  return getItem(StorageKeys.devKey) ?? ''
}

export function setDevKey(value: string): void {
  if (value) setItem(StorageKeys.devKey, value)
  else removeItem(StorageKeys.devKey)
}

export function getTorrServerUrl(): string {
  return getItem(StorageKeys.torrServerUrl) ?? ''
}

export function getTorrServerLogin(): string {
  return getItem(StorageKeys.torrServerLogin) ?? ''
}

export function getTorrServerPassword(): string {
  return getItem(StorageKeys.torrServerPassword) ?? ''
}

export function setTorrServerCreds(
  url: string,
  login: string,
  password: string,
): void {
  if (url) setItem(StorageKeys.torrServerUrl, url)
  else removeItem(StorageKeys.torrServerUrl)
  if (login) setItem(StorageKeys.torrServerLogin, login)
  else removeItem(StorageKeys.torrServerLogin)
  if (password) setItem(StorageKeys.torrServerPassword, password)
  else removeItem(StorageKeys.torrServerPassword)
}
