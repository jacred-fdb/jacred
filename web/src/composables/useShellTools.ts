import { onUnmounted, ref, type Ref } from 'vue'

type SaveHandler = () => void

const apiKeyOpen = ref(false)
const devKeyOpen = ref(false)
const torrServerOpen = ref(false)
const shortcutsOpen = ref(false)

const apiKeySaveHandlers = new Set<SaveHandler>()
const devKeySaveHandlers = new Set<SaveHandler>()

/**
 * Shared shell dialogs (API key, Dev key, TorrServer, shortcuts)
 * and save-notification fan-out for pages that need to reload after a key change.
 */
export function useShellTools() {
  function openApiKey() {
    apiKeyOpen.value = true
  }

  function openDevKey() {
    devKeyOpen.value = true
  }

  function openTorrServer() {
    torrServerOpen.value = true
  }

  function openShortcuts() {
    shortcutsOpen.value = true
  }

  function anyDialogOpen() {
    return (
      apiKeyOpen.value ||
      devKeyOpen.value ||
      torrServerOpen.value ||
      shortcutsOpen.value
    )
  }

  /** Register a page callback when API key is saved from the shell dialog. */
  function onApiKeySaved(handler: SaveHandler) {
    apiKeySaveHandlers.add(handler)
    onUnmounted(() => {
      apiKeySaveHandlers.delete(handler)
    })
  }

  /** Register a page callback when Dev key is saved from the shell dialog. */
  function onDevKeySaved(handler: SaveHandler) {
    devKeySaveHandlers.add(handler)
    onUnmounted(() => {
      devKeySaveHandlers.delete(handler)
    })
  }

  function notifyApiKeySaved() {
    for (const handler of apiKeySaveHandlers) handler()
  }

  function notifyDevKeySaved() {
    for (const handler of devKeySaveHandlers) handler()
  }

  return {
    apiKeyOpen,
    devKeyOpen,
    torrServerOpen,
    shortcutsOpen,
    openApiKey,
    openDevKey,
    openTorrServer,
    openShortcuts,
    anyDialogOpen,
    onApiKeySaved,
    onDevKeySaved,
    notifyApiKeySaved,
    notifyDevKeySaved,
  }
}

export type ShellTools = {
  apiKeyOpen: Ref<boolean>
  devKeyOpen: Ref<boolean>
  torrServerOpen: Ref<boolean>
  shortcutsOpen: Ref<boolean>
}
