import {
  onBeforeUnmount,
  onMounted,
  ref,
  watch,
  type Ref,
} from 'vue'
import {
  densityFromDockWidth,
  densityRank,
  nextDensity,
  type ToolbarDensity,
} from '@/composables/toolbarDensity'

const DEBOUNCE_MS = 50

/**
 * Keeps `data-toolbar-density` on the search dock in sync with available width.
 * CSS container queries set the ceiling; this observer steps down on overflow
 * (and back up when spare room returns) so long RU labels never leave the island.
 */
export function useToolbarDensity(options: {
  dockRef: Ref<HTMLElement | null>
  bandRef: Ref<HTMLElement | null>
}) {
  const density = ref<ToolbarDensity>('regular')
  let timer: ReturnType<typeof setTimeout> | null = null
  let observer: ResizeObserver | null = null

  function writeAttr(value: ToolbarDensity) {
    options.dockRef.value?.setAttribute('data-toolbar-density', value)
  }

  function measure(allowUp = true) {
    const dock = options.dockRef.value
    const band = options.bandRef.value
    if (!dock || !band) return

    const baseline = densityFromDockWidth(dock.clientWidth)
    // Ceiling from dock width (matches @container tiers).
    if (densityRank(density.value) > densityRank(baseline)) {
      density.value = baseline
      writeAttr(baseline)
    } else if (!dock.dataset.toolbarDensity) {
      density.value = baseline
      writeAttr(baseline)
    }

    const overflowing = band.scrollWidth > band.clientWidth + 1
    const spare = band.clientWidth - band.scrollWidth
    let next = nextDensity(
      overflowing,
      density.value,
      allowUp ? spare : 0,
    )
    if (densityRank(next) > densityRank(baseline)) next = baseline

    const changed = next !== density.value
    density.value = next
    writeAttr(next)
    // Labels hide/show after paint — cascade downs only (avoid up/down oscillation).
    if (changed) {
      requestAnimationFrame(() => measure(false))
    }
  }

  function schedule() {
    if (timer) clearTimeout(timer)
    timer = setTimeout(() => {
      timer = null
      measure()
    }, DEBOUNCE_MS)
  }

  function bind() {
    observer?.disconnect()
    const dock = options.dockRef.value
    if (!dock || typeof ResizeObserver === 'undefined') return
    observer = new ResizeObserver(schedule)
    observer.observe(dock)
    const band = options.bandRef.value
    if (band) observer.observe(band)
    // Double rAF so first paint has labels from CQ before we measure overflow.
    requestAnimationFrame(() => requestAnimationFrame(() => measure()))
  }

  onMounted(() => {
    bind()
  })

  watch([options.dockRef, options.bandRef], () => {
    bind()
  })

  onBeforeUnmount(() => {
    if (timer) clearTimeout(timer)
    observer?.disconnect()
    observer = null
  })

  return { measure }
}
