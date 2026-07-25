import { createI18n } from 'vue-i18n'
import { getItem, setItem, StorageKeys } from '@/lib/storage'
import en from '@/i18n/en'
import ru from '@/i18n/ru'

export type AppLocale = 'ru' | 'en'

export function resolveLocale(): AppLocale {
  const saved = getItem(StorageKeys.locale)
  if (saved === 'en' || saved === 'ru') return saved
  const nav = typeof navigator !== 'undefined' ? navigator.language : 'ru'
  return nav.toLowerCase().startsWith('en') ? 'en' : 'ru'
}

export function persistLocale(locale: AppLocale) {
  setItem(StorageKeys.locale, locale)
  document.documentElement.lang = locale
}

const i18n = createI18n({
  legacy: false,
  locale: resolveLocale(),
  fallbackLocale: 'ru',
  messages: { ru, en },
})

persistLocale(i18n.global.locale.value as AppLocale)

export default i18n
