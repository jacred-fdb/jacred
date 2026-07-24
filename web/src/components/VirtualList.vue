<script setup lang="ts" generic="T">
import { useWindowVirtualizer } from '@tanstack/vue-virtual'
import { useEventListener } from '@vueuse/core'
import type { ComponentPublicInstance } from 'vue'
import {
  computed,
  nextTick,
  onActivated,
  onBeforeUnmount,
  onMounted,
  ref,
  toRef,
  watch,
} from 'vue'
import { JR_VIRTUAL_REMEASURE } from '@/lib/result-layout'

const props = withDefaults(
  defineProps<{
    items: T[]
    estimateSize?: number
    overscan?: number
    gap?: number
  }>(),
  {
    estimateSize: 108,
    overscan: 12,
    gap: 10,
  },
)

const listRef = ref<HTMLElement | null>(null)
const itemsRef = toRef(props, 'items')

/**
 * Document Y of the list root — must stay stable during scroll.
 * Recomputing from getBoundingClientRect on every scroll (iOS sticky + URL bar)
 * shifts all translateY values and causes row overlap.
 */
const scrollMargin = ref(0)
let listResizeObserver: ResizeObserver | null = null
let chromeResizeObserver: ResizeObserver | null = null

/**
 * Layout-document Y via offsetParent walk — stable across iOS/iPadOS URL-bar
 * / visualViewport shifts (unlike getBoundingClientRect().top + scrollY).
 */
function documentOffsetTop(el: HTMLElement | null): number {
  if (!el || typeof window === 'undefined') return 0
  let top = 0
  let node: HTMLElement | null = el
  while (node) {
    top += node.offsetTop
    const parent: Element | null = node.offsetParent
    node = parent instanceof HTMLElement ? parent : null
  }
  return Math.max(0, top)
}

function refreshScrollMargin(): boolean {
  const next = documentOffsetTop(listRef.value)
  if (next !== scrollMargin.value) {
    scrollMargin.value = next
    return true
  }
  return false
}

const virtualizer = useWindowVirtualizer(
  computed(() => ({
    count: itemsRef.value.length,
    estimateSize: () => props.estimateSize,
    overscan: props.overscan,
    gap: props.gap,
    scrollMargin: scrollMargin.value,
    // offsetHeight avoids WebKit getBoundingClientRect issues on transformed rows
    measureElement: (element: Element) =>
      (element as HTMLElement).offsetHeight,
  })),
)

const virtualItems = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())
const activeScrollMargin = computed(
  () => virtualizer.value.options.scrollMargin ?? 0,
)

function measureRow(el: Element | ComponentPublicInstance | null) {
  const node =
    el instanceof HTMLElement
      ? el
      : el && '$el' in el && el.$el instanceof HTMLElement
        ? el.$el
        : null
  if (node) virtualizer.value.measureElement(node)
}

/**
 * Remeasure virtualizer. Pass force=true after orientation / KeepAlive / item
 * count changes. visualViewport URL-bar noise only remasures when margin moves.
 */
function syncScrollMargin(force = false) {
  const changed = refreshScrollMargin()
  if (!changed && !force) return
  virtualizer.value.measure()
  // Double-rAF: wait for sticky chrome + visualViewport to settle (Safari/Edge).
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      if (refreshScrollMargin() || force) {
        virtualizer.value.measure()
      }
    })
  })
}

function getFirstVisibleIndex() {
  const items = virtualizer.value.getVirtualItems()
  return items[0]?.index ?? 0
}

function scrollToIndex(
  index: number,
  align: 'start' | 'center' | 'end' | 'auto' = 'start',
) {
  const max = Math.max(0, itemsRef.value.length - 1)
  const clamped = Math.min(Math.max(0, index), max)
  virtualizer.value.scrollToIndex(clamped, { align })
}

function scrollToStart() {
  window.scrollTo({ top: 0, left: 0, behavior: 'auto' })
  document.documentElement.scrollTop = 0
  document.body.scrollTop = 0
}

function observeChrome() {
  if (typeof ResizeObserver === 'undefined') return
  chromeResizeObserver?.disconnect()
  chromeResizeObserver = new ResizeObserver(() => {
    syncScrollMargin()
  })
  const header = document.querySelector('header.jr-glass-nav')
  const dock = document.querySelector('.jr-search-dock')
  const filters = document.querySelector('.jr-filters-panel')
  if (header) chromeResizeObserver.observe(header)
  if (dock) chromeResizeObserver.observe(dock)
  if (filters) chromeResizeObserver.observe(filters)
}

onMounted(() => {
  refreshScrollMargin()
  if (typeof ResizeObserver === 'undefined') return

  listResizeObserver = new ResizeObserver(() => {
    syncScrollMargin()
  })
  if (listRef.value) listResizeObserver.observe(listRef.value)
  observeChrome()
})

watch(listRef, (el, prev) => {
  if (!listResizeObserver) return
  if (prev) listResizeObserver.unobserve(prev)
  if (el) {
    listResizeObserver.observe(el)
    syncScrollMargin()
  }
})

onBeforeUnmount(() => {
  listResizeObserver?.disconnect()
  chromeResizeObserver?.disconnect()
  listResizeObserver = null
  chromeResizeObserver = null
})

useEventListener(
  window,
  'resize',
  () => {
    void nextTick(() => syncScrollMargin(true))
  },
  { passive: true },
)

useEventListener(
  window,
  'orientationchange',
  () => {
    void nextTick(() => syncScrollMargin(true))
  },
  { passive: true },
)

const visualViewport =
  typeof window !== 'undefined' ? window.visualViewport : null

// resize only — visualViewport scroll fires constantly with URL-bar and does
// not change layout document offset when using offsetParent walk.
useEventListener(
  visualViewport,
  'resize',
  () => {
    void nextTick(() => syncScrollMargin())
  },
  { passive: true },
)

useEventListener(window, JR_VIRTUAL_REMEASURE, () => {
  void nextTick(() => syncScrollMargin(true))
})

onActivated(() => {
  void nextTick(() => syncScrollMargin(true))
})

watch(
  () => [props.items.length, props.estimateSize, props.gap] as const,
  async () => {
    await nextTick()
    syncScrollMargin(true)
  },
)

defineExpose({
  listRef,
  virtualizer,
  syncScrollMargin,
  getFirstVisibleIndex,
  scrollToIndex,
  scrollToStart,
  observeChrome,
})
</script>

<template>
  <div ref="listRef" class="relative w-full">
    <div
      role="list"
      class="relative w-full"
      :style="{ height: `${totalSize}px` }"
    >
      <div
        v-for="row in virtualItems"
        :key="String(row.key)"
        :ref="measureRow"
        :data-index="row.index"
        class="absolute top-0 left-0 w-full"
        :style="{
          transform: `translateY(${row.start - activeScrollMargin}px)`,
        }"
      >
        <slot
          :item="items[row.index]!"
          :index="row.index"
        />
      </div>
    </div>
    <slot name="footer" />
  </div>
</template>
