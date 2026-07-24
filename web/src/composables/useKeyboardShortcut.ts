import { onMounted, onUnmounted } from 'vue'

export function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
    ? true
    : target.isContentEditable
}

export function useKeyboardShortcut(handler: (event: KeyboardEvent) => void) {
  onMounted(() => window.addEventListener('keydown', handler))
  onUnmounted(() => window.removeEventListener('keydown', handler))
}
