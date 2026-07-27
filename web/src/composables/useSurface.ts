import { computed, ref } from 'vue'
import {
  getSurface,
  setSurface as persistSurface,
  type SurfaceValue,
} from '@/lib/storage'

const surface = ref<SurfaceValue>(getSurface())

function applySurface(value: SurfaceValue) {
  document.documentElement.dataset.surface = value
  persistSurface(value)
}

applySurface(surface.value)

/** Solid vs glass functional chrome + atmosphere; content stays solid. */
export function useSurface() {
  function setSurface(value: SurfaceValue) {
    surface.value = value
    applySurface(value)
  }

  return {
    surface: computed(() => surface.value),
    isGlass: computed(() => surface.value === 'glass'),
    setSurface,
  }
}
