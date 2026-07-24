import { createApp } from 'vue'
import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { registerSW } from 'virtual:pwa-register'
import { toast } from 'vue-sonner'
import 'vue-sonner/style.css'
import App from './App.vue'
import i18n from './i18n'
import router from './router'
import './style.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
  },
})

createApp(App)
  .use(router)
  .use(i18n)
  .use(VueQueryPlugin, { queryClient })
  .mount('#app')

const updateSW = registerSW({
  immediate: true,
  onNeedRefresh() {
    toast.info(i18n.global.t('app.updateAvailable'), {
      duration: Infinity,
      action: {
        label: i18n.global.t('app.update'),
        onClick: () => void updateSW(true),
      },
    })
  },
  onOfflineReady() {
    toast.success(i18n.global.t('app.offlineReady'))
  },
  onRegisterError(error) {
    console.error('Service worker registration failed', error)
    toast.error(i18n.global.t('app.pwaError'))
  },
})
