<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Check, Copy, Fingerprint, Link2, Loader2, Server } from '@lucide/vue'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
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
  <div class="jr-action-rail" @click.stop>
    <Tooltip v-if="hasMagnet">
      <TooltipTrigger as-child>
        <a
          :href="magnet"
          rel="noopener noreferrer"
          class="jr-action jr-action--lg jr-action--magnet"
          :aria-label="t('search.card.openMagnet')"
        >
          <Link2 :class="iconClass" />
        </a>
      </TooltipTrigger>
      <TooltipContent>{{ t('search.card.openMagnet') }}</TooltipContent>
    </Tooltip>
    <Tooltip v-else>
      <TooltipTrigger as-child>
        <button
          type="button"
          disabled
          class="jr-action jr-action--lg jr-action--magnet"
          :aria-label="t('search.card.magnetUnavailable')"
        >
          <Link2 :class="iconClass" />
        </button>
      </TooltipTrigger>
      <TooltipContent>{{ t('search.card.magnetUnavailable') }}</TooltipContent>
    </Tooltip>

    <Tooltip>
      <TooltipTrigger as-child>
        <button
          type="button"
          class="jr-action jr-action--copy"
          :disabled="!hasMagnet"
          :aria-label="t('search.card.copyMagnet')"
          @click="emit('copyMagnet')"
        >
          <Copy :class="iconClass" />
        </button>
      </TooltipTrigger>
      <TooltipContent>{{ t('search.card.copyMagnet') }}</TooltipContent>
    </Tooltip>

    <Tooltip>
      <TooltipTrigger as-child>
        <button
          type="button"
          class="jr-action jr-action--hash"
          :disabled="!hasMagnet"
          :aria-label="t('search.card.copyHash')"
          @click="emit('copyHash')"
        >
          <Fingerprint :class="iconClass" />
        </button>
      </TooltipTrigger>
      <TooltipContent>{{ t('search.card.copyHash') }}</TooltipContent>
    </Tooltip>

    <Tooltip>
      <TooltipTrigger as-child>
        <button
          type="button"
          class="jr-action jr-action--tor"
          :disabled="!hasMagnet || torState === 'loading'"
          :aria-label="t('search.card.torrServer')"
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
      </TooltipTrigger>
      <TooltipContent>{{ t('search.card.torrServer') }}</TooltipContent>
    </Tooltip>
  </div>
</template>
