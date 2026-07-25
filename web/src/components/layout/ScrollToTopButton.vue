<script setup lang="ts">
import { useWindowScroll } from '@vueuse/core'
import { computed } from 'vue'
import { ArrowUp } from '@lucide/vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { JR_VIRTUAL_REMEASURE } from '@/lib/result-layout'

const { t } = useI18n()
const { y } = useWindowScroll()
const visible = computed(() => y.value > 400)

function scrollToTop() {
  // Instant jump to true document top (smooth + virtualizer remasure fought each other).
  window.scrollTo(0, 0)
  document.documentElement.scrollTop = 0
  document.body.scrollTop = 0
  window.dispatchEvent(new Event(JR_VIRTUAL_REMEASURE))
}
</script>

<template>
  <Transition name="jr-fade">
    <Button
      v-if="visible"
      type="button"
      variant="secondary"
      size="icon"
      class="jr-scroll-top fixed z-50 size-11 rounded-full border shadow-lg"
      :aria-label="t('app.backToTop')"
      @click="scrollToTop"
    >
      <ArrowUp class="size-4" />
    </Button>
  </Transition>
</template>
