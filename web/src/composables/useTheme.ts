import { computed, ref, watch } from 'vue'
import {
  getTheme,
  setTheme as persistTheme,
  type ThemeValue,
} from '@/lib/storage'

/** Keep in sync with --background in style.css (Apple HIG surfaces) */
const THEME_COLOR_DARK = '#000000'
const THEME_COLOR_LIGHT = '#f2f2f7'

const theme = ref<ThemeValue>(getTheme())

function applyTheme(value: ThemeValue) {
  const root = document.documentElement
  root.classList.toggle('dark', value === 'dark')
  root.dataset.theme = value
  persistTheme(value)

  const color = value === 'dark' ? THEME_COLOR_DARK : THEME_COLOR_LIGHT
  document.querySelectorAll('meta[name="theme-color"]').forEach((meta) => {
    meta.setAttribute('content', color)
  })

  const tile = document.querySelector('meta[name="msapplication-TileColor"]')
  if (tile) tile.setAttribute('content', color)

  const statusBar = document.querySelector(
    'meta[name="apple-mobile-web-app-status-bar-style"]',
  )
  if (statusBar) {
    // Always translucent with viewport-fit=cover + header safe-area padding
    // (light `default` fights edge-to-edge layout in standalone PWA).
    statusBar.setAttribute('content', 'black-translucent')
  }
}

applyTheme(theme.value)

watch(theme, (value) => applyTheme(value))

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
