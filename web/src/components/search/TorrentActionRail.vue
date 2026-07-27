<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Check, Copy, Fingerprint, Link2, Loader2, Server } from '@lucide/vue'
import { cn } from '@/lib/utils'

defineProps<{
  magnet: string
  hasMagnet: boolean
  torState: 'idle' | 'loading' | 'success'
}>()

const emit = defineEmits<{
  copyMagnet: []
  copyHash: []
  sendTorr: []
}>()

const { t } = useI18n()
const iconClass = 'size-4'
</script>

<template>
  <!-- aria-label only — Tooltips × N cards are too expensive -->
  <div class="jr-action-rail" @click.stop>
    <a
      v-if="hasMagnet"
      :href="magnet"
      rel="noopener noreferrer"
      class="jr-action jr-action--lg jr-action--magnet"
      :aria-label="t('search.card.openMagnet')"
      :title="t('search.card.openMagnet')"
    >
      <Link2 :class="iconClass" />
    </a>
    <button
      v-else
      type="button"
      disabled
      class="jr-action jr-action--lg jr-action--magnet"
      :aria-label="t('search.card.magnetUnavailable')"
      :title="t('search.card.magnetUnavailable')"
    >
      <Link2 :class="iconClass" />
    </button>

    <button
      type="button"
      class="jr-action jr-action--copy"
      :disabled="!hasMagnet"
      :aria-label="t('search.card.copyMagnet')"
      :title="t('search.card.copyMagnet')"
      @click="emit('copyMagnet')"
    >
      <Copy :class="iconClass" />
    </button>

    <button
      type="button"
      class="jr-action jr-action--hash"
      :disabled="!hasMagnet"
      :aria-label="t('search.card.copyHash')"
      :title="t('search.card.copyHash')"
      @click="emit('copyHash')"
    >
      <Fingerprint :class="iconClass" />
    </button>

    <button
      type="button"
      class="jr-action jr-action--tor"
      :disabled="!hasMagnet || torState === 'loading'"
      :aria-label="t('search.card.torrServer')"
      :title="t('search.card.torrServer')"
      @click="emit('sendTorr')"
    >
      <Loader2
        v-if="torState === 'loading'"
        :class="cn(iconClass, 'animate-spin')"
      />
      <Check
        v-else-if="torState === 'success'"
        :class="cn(iconClass, 'text-[var(--success)]')"
      />
      <Server v-else :class="iconClass" />
    </button>
  </div>
</template>
