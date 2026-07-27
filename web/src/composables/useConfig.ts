import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { apiClient, ApiError } from '@/lib/api/client'
import { useShellTools } from '@/composables/useShellTools'
import {
  authErrorKind,
  deepClone,
  resolveActiveTab,
  setByPath,
  tabIdForGroup,
  type AuthErrorKind,
  type ConfigDiffResponse,
  type ConfigFormat,
  type ConfigSchema,
  type ConfigValidation,
} from '@/lib/config-schema'
import {
  getDevKey,
  getItem,
  setItem,
  StorageKeys,
} from '@/lib/storage'

export type SettingsMode = 'form' | 'raw'

/** Settings page state: schema/config load, form+raw modes, validation, and save flow. */
export function useConfig() {
  const { t } = useI18n()
  const shell = useShellTools()

  const mode = ref<SettingsMode>(
    getItem(StorageKeys.settingsMode) === 'raw' ? 'raw' : 'form',
  )
  const format = ref<ConfigFormat>('yaml')
  const schema = ref<ConfigSchema | null>(null)
  const formData = ref<Record<string, unknown>>({})
  const rawContent = ref('')
  const path = ref('')
  const lastModifiedUtc = ref<string | undefined>()
  const activeTab = ref<string | null>(getItem(StorageKeys.settingsFormTab))

  const isLoading = ref(false)
  const isBusy = ref(false)
  const dirty = ref(false)
  const accessDenied = ref(false)
  const accessKind = ref<AuthErrorKind>('network')
  const validation = ref<ConfigValidation | null>(null)
  const errorMessage = ref('')

  const diffDialogOpen = ref(false)
  const pendingDiff = ref<ConfigDiffResponse | null>(null)
  const pendingSave = ref<{
    data: Record<string, unknown>
    format: ConfigFormat
  } | null>(null)

  const hasEditor = computed(() => !accessDenied.value && !!schema.value)

  function markDirty() {
    dirty.value = true
  }

  function updateField(path: string, value: unknown) {
    setByPath(formData.value, path, value)
    markDirty()
  }

  function setMode(next: SettingsMode) {
    mode.value = next
    setItem(StorageKeys.settingsMode, next)
  }

  function setActiveTab(tabId: string) {
    activeTab.value = tabId
    setItem(StorageKeys.settingsFormTab, tabId)
  }

  function handleAuth(status: number, { prompt = false } = {}) {
    const kind = authErrorKind(status, !!getDevKey())
    accessKind.value = kind
    accessDenied.value = true
    schema.value = null
    if (prompt && kind === 'devkey') shell.openDevKey()
  }

  async function normalizePayload(): Promise<{
    data: Record<string, unknown>
    format: ConfigFormat
  }> {
    const fmt = format.value
    if (mode.value === 'form') {
      return { data: deepClone(formData.value), format: fmt }
    }
    const parsed = await apiClient.postConfigParse({
      content: rawContent.value,
      format: fmt,
    })
    if (!parsed.ok || !parsed.data) {
      throw new Error(parsed.error || t('settings.messages.parseFailed'))
    }
    return { data: parsed.data, format: fmt }
  }

  async function loadConfig({ promptDevKey = true } = {}) {
    isLoading.value = true
    errorMessage.value = ''
    validation.value = null
    try {
      const json = await apiClient.getConfig({ format: format.value })
      if (!json.ok && 'error' in json && typeof json.error === 'string') {
        throw new Error(json.error)
      }

      accessDenied.value = false
      schema.value = (json.schema as ConfigSchema | undefined) ?? null
      formData.value = deepClone(json.data ?? {})
      rawContent.value = json.content ?? ''
      path.value = json.path ?? ''
      lastModifiedUtc.value = json.lastModifiedUtc
      if (json.format === 'json' || json.format === 'yaml') {
        format.value = json.format
      }
      const resolved = resolveActiveTab(schema.value, activeTab.value)
      if (resolved) activeTab.value = resolved
      else if (schema.value?.groups?.[0]) {
        activeTab.value = tabIdForGroup(schema.value.groups[0])
      }
      dirty.value = false
      document.title = t('settings.pageTitle')
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        handleAuth(err.status, { prompt: promptDevKey })
        return
      }
      errorMessage.value =
        err instanceof Error ? err.message : t('settings.messages.loadFailed')
      toast.error(errorMessage.value)
    } finally {
      isLoading.value = false
    }
  }

  async function switchMode(next: SettingsMode) {
    if (next === mode.value || isBusy.value) return
    isBusy.value = true
    try {
      if (next === 'raw' && mode.value === 'form') {
        const rendered = await apiClient.postConfigRender({
          data: formData.value,
          format: format.value,
        })
        if (!rendered.ok) {
          throw new Error(rendered.error || t('settings.messages.renderFailed'))
        }
        rawContent.value = rendered.content ?? ''
      } else if (next === 'form' && mode.value === 'raw') {
        const parsed = await apiClient.postConfigParse({
          content: rawContent.value,
          format: format.value,
        })
        if (!parsed.ok || !parsed.data) {
          throw new Error(parsed.error || t('settings.messages.parseFailed'))
        }
        formData.value = deepClone(parsed.data)
      }
      setMode(next)
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        handleAuth(err.status, { prompt: true })
        return
      }
      toast.error(
        err instanceof Error ? err.message : t('settings.messages.modeFailed'),
      )
    } finally {
      isBusy.value = false
    }
  }

  async function onFormatChange(next: ConfigFormat) {
    if (next === format.value) return
    const prev = format.value
    format.value = next
    if (mode.value !== 'raw') return
    isBusy.value = true
    try {
      const rendered = await apiClient.postConfigRender({
        data: formData.value,
        format: next,
      })
      if (!rendered.ok) {
        throw new Error(rendered.error || t('settings.messages.renderFailed'))
      }
      rawContent.value = rendered.content ?? ''
      markDirty()
    } catch (err) {
      format.value = prev
      toast.error(
        err instanceof Error
          ? err.message
          : t('settings.messages.formatChangeFailed'),
      )
    } finally {
      isBusy.value = false
    }
  }

  async function validate() {
    isBusy.value = true
    validation.value = null
    try {
      const payload = await normalizePayload()
      const result = await apiClient.postConfigValidate(payload)
      validation.value = result
      if (result.ok && !(result.warnings?.length)) {
        toast.success(t('settings.messages.valid'))
      } else if (result.ok) {
        toast.message(t('settings.messages.hasWarnings'))
      } else {
        toast.error(t('settings.messages.hasErrors'))
      }
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        handleAuth(err.status, { prompt: true })
        return
      }
      toast.error(
        err instanceof Error
          ? err.message
          : t('settings.messages.validateFailed'),
      )
    } finally {
      isBusy.value = false
    }
  }

  async function formatConfig({ switchToRaw = false } = {}) {
    isBusy.value = true
    try {
      const payload = await normalizePayload()
      const result = await apiClient.postConfigFormat(payload)
      if (!result.ok) {
        throw new Error(result.error || t('settings.messages.formatFailed'))
      }
      if (result.data) formData.value = deepClone(result.data)
      rawContent.value = result.content ?? ''
      if (result.format === 'yaml' || result.format === 'json') {
        format.value = result.format
      }
      markDirty()
      if (switchToRaw || mode.value === 'form') setMode('raw')
      toast.success(t('settings.messages.formatted'))
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        handleAuth(err.status, { prompt: true })
        return
      }
      toast.error(
        err instanceof Error ? err.message : t('settings.messages.formatFailed'),
      )
    } finally {
      isBusy.value = false
    }
  }

  async function prepareSave() {
    isBusy.value = true
    try {
      const payload = await normalizePayload()
      const diff = await apiClient.postConfigDiff(payload)
      pendingDiff.value = diff
      pendingSave.value = payload
      diffDialogOpen.value = true
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        handleAuth(err.status, { prompt: true })
        return
      }
      toast.error(
        err instanceof Error ? err.message : t('settings.messages.diffFailed'),
      )
    } finally {
      isBusy.value = false
    }
  }

  async function confirmSave() {
    if (!pendingSave.value) return
    const validationOk = pendingDiff.value?.validation?.ok !== false
    if (!validationOk) {
      toast.error(t('settings.messages.saveBlocked'))
      return
    }
    isBusy.value = true
    try {
      const result = await apiClient.postConfigSave(pendingSave.value)
      if (!result.ok) {
        throw new Error(
          result.error || result.message || t('settings.messages.saveFailed'),
        )
      }
      toast.success(result.message || t('settings.messages.saved'))
      diffDialogOpen.value = false
      pendingDiff.value = null
      pendingSave.value = null
      await loadConfig({ promptDevKey: false })
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        handleAuth(err.status, { prompt: true })
        return
      }
      toast.error(
        err instanceof Error ? err.message : t('settings.messages.saveFailed'),
      )
    } finally {
      isBusy.value = false
    }
  }

  function onDevKeySaved() {
    toast.success(t('settings.messages.devKeySaved'))
    void loadConfig({ promptDevKey: false })
  }

  shell.onDevKeySaved(onDevKeySaved)

  function onBeforeUnload(e: BeforeUnloadEvent) {
    if (!dirty.value) return
    e.preventDefault()
    e.returnValue = ''
  }

  function reload() {
    if (dirty.value && !window.confirm(t('settings.messages.reloadConfirm'))) {
      return
    }
    void loadConfig({ promptDevKey: false })
  }

  watch(mode, (m) => setItem(StorageKeys.settingsMode, m))

  onMounted(() => {
    window.addEventListener('beforeunload', onBeforeUnload)
    void loadConfig()
  })

  onUnmounted(() => {
    window.removeEventListener('beforeunload', onBeforeUnload)
  })

  onBeforeRouteLeave((_to, _from, next) => {
    if (
      dirty.value &&
      !window.confirm(t('settings.messages.reloadConfirm'))
    ) {
      next(false)
      return
    }
    next()
  })

  return {
    mode,
    format,
    schema,
    formData,
    rawContent,
    path,
    lastModifiedUtc,
    activeTab,
    isLoading,
    isBusy,
    dirty,
    accessDenied,
    accessKind,
    accessMessage: computed(() => t(`settings.access.${accessKind.value}`)),
    validation,
    errorMessage,
    hasEditor,
    diffDialogOpen,
    pendingDiff,
    markDirty,
    updateField,
    setMode,
    setActiveTab,
    switchMode,
    onFormatChange,
    validate,
    formatConfig,
    prepareSave,
    confirmSave,
    loadConfig,
    reload,
    openApiKey: shell.openApiKey,
    openDevKey: shell.openDevKey,
  }
}
