import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'search',
      component: () => import('@/pages/SearchPage.vue'),
      meta: { titleKey: 'search.pageTitle' },
    },
    {
      path: '/stats',
      name: 'stats',
      component: () => import('@/pages/StatsPage.vue'),
      meta: { titleKey: 'stats.pageTitle' },
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('@/pages/SettingsPage.vue'),
      meta: { titleKey: 'settings.pageTitle' },
    },
  ],
  scrollBehavior(to, from, savedPosition) {
    if (savedPosition) return savedPosition
    // Same Search route with only query updates (new search) — keep scroll.
    if (to.name === 'search' && from.name === 'search') return false
    // KeepAlive Search: preserve scroll when returning from Stats/Settings.
    if (to.name === 'search' && from.name && from.name !== 'search') {
      return false
    }
    return { top: 0 }
  },
})

export default router
