import { watchEffect } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'

export function usePageTitle() {
  const route = useRoute()
  const { t, locale } = useI18n()

  watchEffect(() => {
    void locale.value
    const search =
      typeof route.query.s === 'string' ? route.query.s.trim() : ''
    if (route.name === 'search' && search) {
      document.title = t('search.documentTitle', { query: search })
      return
    }
    const key =
      route.name === 'stats'
        ? 'nav.stats'
        : route.name === 'settings'
          ? 'nav.settings'
          : 'nav.search'
    document.title = `${t(key)} — ${t('app.name')}`
  })
}
