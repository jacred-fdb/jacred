import { onMounted, onUnmounted } from 'vue'

/** True when the event target is an editable control (skip global shortcuts). */
export function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
    ? true
    : target.isContentEditable
}

/** Register a window keydown listener for the component lifetime. */
export function useKeyboardShortcut(handler: (event: KeyboardEvent) => void) {
  onMounted(() => window.addEventListener('keydown', handler))
  onUnmounted(() => window.removeEventListener('keydown', handler))
}
