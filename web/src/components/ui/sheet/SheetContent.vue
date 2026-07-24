<script setup lang="ts">
import type { DialogContentEmits, DialogContentProps } from 'reka-ui'

import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { XIcon } from '@lucide/vue'
import { reactiveOmit } from '@vueuse/core'
import {
  DialogClose,
  DialogContent,
  DialogPortal,
  useForwardPropsEmits,
} from 'reka-ui'
import { useI18n } from 'vue-i18n'
import { useSheetDrag } from '@/composables/useSheetDrag'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import SheetOverlay from './SheetOverlay.vue'

interface SheetContentProps extends DialogContentProps {
  class?: HTMLAttributes['class']
  side?: 'top' | 'right' | 'bottom' | 'left'
  showCloseButton?: boolean
}

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(defineProps<SheetContentProps>(), {
  side: 'right',
  showCloseButton: true,
})
const emits = defineEmits<DialogContentEmits>()

const { t } = useI18n()
const delegatedProps = reactiveOmit(props, 'class', 'side', 'showCloseButton')
const forwarded = useForwardPropsEmits(delegatedProps, emits)

const isBottom = computed(() => props.side === 'bottom')
const { onPointerDown, onPointerMove, onPointerUp, onPointerCancel } =
  useSheetDrag(isBottom)
</script>

<template>
  <DialogPortal>
    <SheetOverlay />
    <DialogContent
      data-slot="sheet-content"
      :data-side="side"
      :class="
        cn(
          'jr-glass text-popover-foreground fixed z-50 flex flex-col gap-4 bg-clip-padding text-sm shadow-lg transition duration-200 ease-in-out',
          'data-[side=bottom]:inset-x-0 data-[side=bottom]:bottom-0 data-[side=bottom]:h-auto data-[side=bottom]:max-h-[min(92dvh,40rem)] data-[side=bottom]:border-t data-[side=bottom]:rounded-t-2xl',
          'data-[side=left]:inset-y-0 data-[side=left]:left-0 data-[side=left]:h-full data-[side=left]:w-3/4 data-[side=left]:border-r',
          'data-[side=right]:inset-y-0 data-[side=right]:right-0 data-[side=right]:h-full data-[side=right]:w-3/4 data-[side=right]:border-l',
          'data-[side=top]:inset-x-0 data-[side=top]:top-0 data-[side=top]:h-auto data-[side=top]:border-b',
          'data-[side=left]:sm:max-w-sm data-[side=right]:sm:max-w-sm',
          'data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0',
          'data-[side=bottom]:data-open:slide-in-from-bottom data-[side=bottom]:data-closed:slide-out-to-bottom',
          'data-[side=left]:data-open:slide-in-from-left data-[side=left]:data-closed:slide-out-to-left',
          'data-[side=right]:data-open:slide-in-from-right data-[side=right]:data-closed:slide-out-to-right',
          'data-[side=top]:data-open:slide-in-from-top data-[side=top]:data-closed:slide-out-to-top',
          props.class,
        )
      "
      v-bind="{ ...$attrs, ...forwarded }"
    >
      <div
        v-if="isBottom"
        class="jr-sheet-handle mx-auto flex w-full max-w-xs shrink-0 cursor-grab touch-none flex-col items-center pt-1 pb-1 active:cursor-grabbing"
        :aria-label="t('search.filters.dragHandle')"
        role="presentation"
        @pointerdown="onPointerDown"
        @pointermove="onPointerMove"
        @pointerup="onPointerUp"
        @pointercancel="onPointerCancel"
      >
        <span
          class="bg-muted-foreground/40 h-1 w-10 rounded-full"
          aria-hidden="true"
        />
      </div>

      <slot />

      <DialogClose
        v-if="showCloseButton"
        data-slot="sheet-close"
        as-child
      >
        <Button variant="ghost" class="absolute top-3 right-3" size="icon-sm">
          <XIcon />
          <span class="sr-only">Close</span>
        </Button>
      </DialogClose>
    </DialogContent>
  </DialogPortal>
</template>
