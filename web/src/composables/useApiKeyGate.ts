import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api/client'
import { getApiKey } from '@/lib/storage'
import { useShellTools } from '@/composables/useShellTools'

const CONF_TIMEOUT_MS = 5_000

/**
 * Ensures search/stats can call authenticated APIs.
 * Opens the API key dialog when the instance requires a key and none is stored.
 */
export function useApiKeyGate() {
  const { t } = useI18n()
  const shell = useShellTools()

  async function ensureApiKey(): Promise<void> {
    const conf = await apiClient.getConf({ timeoutMs: CONF_TIMEOUT_MS })
    const key = getApiKey()
    if (conf.apikey) return
    if (key) throw new Error(t('search.errors.invalidApiKey'))
    shell.openApiKey()
    throw new Error(t('search.errors.apiKeyRequired'))
  }

  return { ensureApiKey }
}
