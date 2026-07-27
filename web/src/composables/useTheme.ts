import { computed, ref, watch } from 'vue'
import {
  getTheme,
  setTheme as persistTheme,
  type ThemeValue,
} from '@/lib/storage'

/** Keep in sync with --background in style.css */
const THEME_COLOR_DARK = '#000000'
const THEME_COLOR_LIGHT = '#f2f2f7'

const theme = ref<ThemeValue>(getTheme())

function applyTheme(value: ThemeValue) {
  const root = document.documentElement
  root.classList.toggle('dark', value === 'dark')
  root.dataset.theme = value
  persistTheme(value)

  const color = value === 'dark' ? THEME_COLOR_DARK : THEME_COLOR_LIGHT
  // Only update metas for the active scheme — don't wipe dual light/dark entries.
  document.querySelectorAll('meta[name="theme-color"]').forEach((meta) => {
    const media = meta.getAttribute('media')
    if (!media) {
      meta.setAttribute('content', color)
      return
    }
    const wantsDark = /prefers-color-scheme:\s*dark/i.test(media)
    const wantsLight = /prefers-color-scheme:\s*light/i.test(media)
    if (value === 'dark' && wantsDark) meta.setAttribute('content', color)
    if (value === 'light' && wantsLight) meta.setAttribute('content', color)
  })

  const tile = document.querySelector('meta[name="msapplication-TileColor"]')
  if (tile) tile.setAttribute('content', color)

  const statusBar = document.querySelector(
    'meta[name="apple-mobile-web-app-status-bar-style"]',
  )
  if (statusBar) {
    // black-translucent works with viewport-fit=cover + safe-area header padding
    statusBar.setAttribute('content', 'black-translucent')
  }
}

applyTheme(theme.value)

watch(theme, (value) => applyTheme(value))

/** Light/dark theme with meta theme-color and status-bar sync for PWA. */
export function useTheme() {
  const isDark = computed(() => theme.value === 'dark')

  function setTheme(value: ThemeValue) {
    theme.value = value
  }

  function toggleTheme() {
    theme.value = theme.value === 'dark' ? 'light' : 'dark'
  }

  return {
    theme: computed(() => theme.value),
    isDark,
    setTheme,
    toggleTheme,
  }
}
