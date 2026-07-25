<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  formatStatNumber,
  formatStatNumberFull,
  type StatsAggregate,
} from '@/lib/stats'
import { cn } from '@/lib/utils'

defineProps<{
  aggregate: StatsAggregate
  fullNumbers: boolean
}>()
const { t, locale } = useI18n()

const primary = computed(() => [
  { key: 'alltorrents' as const, chip: 'total', label: t('stats.card.total') },
  { key: 'newtor' as const, chip: 'new', label: t('stats.card.newToday') },
  { key: 'update' as const, chip: 'update', label: t('stats.card.updated') },
])

const secondary = computed(() => [
  { key: 'wait' as const, chip: 'wait', label: t('stats.card.waiting') },
  { key: 'confirm' as const, chip: 'confirm', label: t('stats.card.confirmed') },
  { key: 'skip' as const, chip: 'skip', label: t('stats.card.skipped') },
])
</script>

<template>
  <section
    class="jr-glass rounded-xl border p-4"
    :aria-label="t('stats.summary')"
  >
    <h2 class="mb-3 text-sm font-semibold text-muted-foreground">
      {{ t('stats.summary') }}
    </h2>
    <div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
      <div
        v-for="m in primary"
        :key="m.key"
        :class="cn('min-w-0 rounded-lg px-3 py-2.5', `jr-stat-chip--${m.chip}`)"
      >
        <div class="text-xs font-medium tracking-wide uppercase opacity-80">
          {{ m.label }}
        </div>
        <div
          class="mt-1 truncate text-2xl font-semibold tabular-nums tracking-tight"
          :aria-label="formatStatNumberFull(aggregate[m.key], locale)"
          :title="formatStatNumberFull(aggregate[m.key], locale)"
        >
          {{ formatStatNumber(aggregate[m.key], fullNumbers, locale) }}
        </div>
      </div>
    </div>
    <div class="mt-3 grid grid-cols-1 gap-1.5 border-t border-border/60 pt-3 sm:grid-cols-3">
      <div
        v-for="m in secondary"
        :key="m.key"
        :class="
          cn(
            'flex min-w-0 items-baseline justify-between gap-2 rounded-md px-2.5 py-1.5',
            `jr-stat-chip--${m.chip}`,
          )
        "
      >
        <span class="text-xs font-medium tracking-wide uppercase opacity-80">{{
          m.label
        }}</span>
        <strong
          class="truncate text-sm font-semibold tabular-nums"
          :aria-label="formatStatNumberFull(aggregate[m.key], locale)"
          :title="formatStatNumberFull(aggregate[m.key], locale)"
        >
          {{ formatStatNumber(aggregate[m.key], fullNumbers, locale) }}
        </strong>
      </div>
    </div>
  </section>
</template>
