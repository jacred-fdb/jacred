import { onMounted } from 'vue'
import { getItem, setItem, StorageKeys } from '@/lib/storage'

/**
 * One-shot cleanup of the legacy hand-rolled /sw.js after SPA cutover.
 * Safe to call in Phase 0 — no-ops until an old worker is registered.
 */
export function useLegacySwCleanup() {
  onMounted(async () => {
    if (getItem(StorageKeys.legacySwCleanupDone) === '1') return
    if (!('serviceWorker' in navigator)) {
      setItem(StorageKeys.legacySwCleanupDone, '1')
      return
    }

    try {
      const registrations = await navigator.serviceWorker.getRegistrations()
      for (const reg of registrations) {
        const scriptUrl =
          reg.active?.scriptURL ??
          reg.waiting?.scriptURL ??
          reg.installing?.scriptURL ??
          ''
        // Legacy worker was exactly /sw.js without Workbox revisioned name
        const isLegacy =
          /\/sw\.js$/i.test(scriptUrl) && !/workbox/i.test(scriptUrl)
        if (isLegacy) {
          await reg.unregister()
        }
      }

      if ('caches' in window) {
        const keys = await caches.keys()
        await Promise.all(
          keys
            .filter((name) => /^jacred-/i.test(name) && !name.includes('static'))
            .map((name) => caches.delete(name)),
        )
      }
    } catch {
      /* ignore */
    } finally {
      setItem(StorageKeys.legacySwCleanupDone, '1')
    }
  })
}
