<script setup lang="ts" generic="T">
import { useWindowVirtualizer } from '@tanstack/vue-virtual'
import { useElementBounding, useEventListener, useWindowScroll } from '@vueuse/core'
import type { ComponentPublicInstance } from 'vue'
import {
  computed,
  nextTick,
  ref,
  toRef,
  watch,
} from 'vue'

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
const { y: scrollY } = useWindowScroll()
const { top } = useElementBounding(listRef)

/** Document offset of the list — TanStack window virtualizer scrollMargin. */
const scrollMargin = computed(() => Math.max(0, top.value + scrollY.value))

const virtualizer = useWindowVirtualizer(
  computed(() => ({
    count: itemsRef.value.length,
    estimateSize: () => props.estimateSize,
    overscan: props.overscan,
    gap: props.gap,
    scrollMargin: scrollMargin.value,
    measureElement:
      typeof window !== 'undefined' &&
      !/Firefox/i.test(navigator.userAgent)
        ? (element: Element) =>
            (element as HTMLElement).getBoundingClientRect().height
        : (element: Element) => (element as HTMLElement).offsetHeight,
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

function syncScrollMargin() {
  // Bounding box is reactive via VueUse; force remasure of row sizes.
  virtualizer.value.measure()
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
  // Align with page chrome: document top, not first virtual row mid-page.
  window.scrollTo({ top: 0, left: 0, behavior: 'auto' })
  document.documentElement.scrollTop = 0
  document.body.scrollTop = 0
}

useEventListener(window, 'resize', () => {
  void nextTick(syncScrollMargin)
}, { passive: true })

watch(
  () => [props.items.length, props.estimateSize, props.gap] as const,
  async () => {
    await nextTick()
    virtualizer.value.measure()
  },
)

defineExpose({
  listRef,
  virtualizer,
  syncScrollMargin,
  getFirstVisibleIndex,
  scrollToIndex,
  scrollToStart,
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
        class="absolute top-0 left-0 w-full will-change-transform"
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
