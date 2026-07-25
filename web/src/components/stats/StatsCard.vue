<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  formatStatNumber,
  formatStatNumberFull,
  getTrackerDisplayName,
  getTracksData,
  type TrackerStat,
} from '@/lib/stats'
import { getSafeIconPath } from '@/lib/torrents'
import { cn } from '@/lib/utils'

const props = defineProps<{
  item: TrackerStat
  fullNumbers: boolean
  compact?: boolean
}>()
const { t, locale } = useI18n()

const tracks = computed(() => getTracksData(props.item))
const slug = computed(() => (props.item.trackerName || '').toLowerCase())
const displayName = computed(() => getTrackerDisplayName(slug.value))
const showSlug = computed(
  () => displayName.value.toLowerCase() !== slug.value,
)
const iconSrc = computed(() => getSafeIconPath(props.item.trackerName))

type Metric = {
  key: string
  label: string
  value: string
  full: string
  chip: string
}

function metric(
  key: string,
  chip: string,
  label: string,
  value: string,
  full: string,
): Metric {
  return { key, chip, label, value, full }
}

const lastNew = computed((): Metric =>
  metric(
    'lastnew',
    'lastnew',
    t('stats.card.lastNew'),
    props.item.lastnewtor || '—',
    props.item.lastnewtor || '—',
  ),
)

const pairs = computed((): Metric[][] => {
  const tracksData = tracks.value
  return [
    [
      metric(
        'new',
        'new',
        t('stats.card.new'),
        formatStatNumber(props.item.newtor, props.fullNumbers, locale.value),
        formatStatNumberFull(props.item.newtor, locale.value),
      ),
      metric(
        'update',
        'update',
        t('stats.card.updated'),
        formatStatNumber(props.item.update, props.fullNumbers, locale.value),
        formatStatNumberFull(props.item.update, locale.value),
      ),
    ],
    [
      metric(
        'total',
        'total',
        t('stats.card.total'),
        formatStatNumber(
          props.item.alltorrents,
          props.fullNumbers,
          locale.value,
        ),
        formatStatNumberFull(props.item.alltorrents, locale.value),
      ),
      metric(
        'confirm',
        'confirm',
        t('stats.card.confirmed'),
        formatStatNumber(tracksData.confirm, props.fullNumbers, locale.value),
        formatStatNumberFull(tracksData.confirm, locale.value),
      ),
    ],
    [
      metric(
        'wait',
        'wait',
        t('stats.card.waiting'),
        formatStatNumber(tracksData.wait, props.fullNumbers, locale.value),
        formatStatNumberFull(tracksData.wait, locale.value),
      ),
      metric(
        'skip',
        'skip',
        t('stats.card.skipped'),
        formatStatNumber(tracksData.skip, props.fullNumbers, locale.value),
        formatStatNumberFull(tracksData.skip, locale.value),
      ),
    ],
  ]
})

const compactMetrics = computed((): Metric[] => {
  const tracksData = tracks.value
  return [
    metric(
      'new',
      'new',
      t('stats.card.new'),
      formatStatNumber(props.item.newtor, props.fullNumbers, locale.value),
      formatStatNumberFull(props.item.newtor, locale.value),
    ),
    metric(
      'update',
      'update',
      t('stats.card.updated'),
      formatStatNumber(props.item.update, props.fullNumbers, locale.value),
      formatStatNumberFull(props.item.update, locale.value),
    ),
    metric(
      'total',
      'total',
      t('stats.card.total'),
      formatStatNumber(props.item.alltorrents, props.fullNumbers, locale.value),
      formatStatNumberFull(props.item.alltorrents, locale.value),
    ),
    metric(
      'confirm',
      'confirm',
      t('stats.card.confirmed'),
      formatStatNumber(tracksData.confirm, props.fullNumbers, locale.value),
      formatStatNumberFull(tracksData.confirm, locale.value),
    ),
  ]
})
</script>

<template>
  <article
    data-result-card
    :class="
      cn(
        'jr-glass rounded-xl border text-card-foreground',
        compact ? 'p-3' : 'p-4',
      )
    "
  >
    <header
      :class="cn('flex items-center gap-2.5', compact ? 'mb-2' : 'mb-3')"
    >
      <img
        :src="iconSrc"
        alt=""
        width="20"
        height="20"
        class="size-5 rounded-sm"
        loading="lazy"
        @error="($event.target as HTMLImageElement).style.visibility = 'hidden'"
      />
      <div class="min-w-0 flex-1">
        <div class="truncate font-semibold">{{ displayName }}</div>
        <div
          v-if="showSlug"
          class="truncate text-xs text-muted-foreground"
        >
          {{ slug }}
        </div>
      </div>
      <div
        v-if="compact"
        class="shrink-0 text-right text-xs tabular-nums text-muted-foreground"
        :title="lastNew.full"
      >
        {{ lastNew.value }}
      </div>
    </header>

    <!-- Mobile list: dense single row of key metrics -->
    <div
      v-if="compact"
      class="flex flex-wrap items-center gap-1.5"
    >
      <div
        v-for="m in compactMetrics"
        :key="m.key"
        :class="
          cn(
            'inline-flex min-w-0 items-baseline gap-1 rounded-md px-2 py-1',
            `jr-stat-chip--${m.chip}`,
          )
        "
      >
        <span class="text-[10px] font-medium tracking-wide uppercase opacity-80">{{
          m.label
        }}</span>
        <strong
          class="text-sm font-semibold tabular-nums"
          :aria-label="m.full"
          :title="m.full"
        >
          {{ m.value }}
        </strong>
      </div>
    </div>

    <div v-else class="space-y-1.5">
      <div
        :class="
          cn(
            'flex min-h-14 flex-col justify-between rounded-lg px-2.5 py-2',
            `jr-stat-chip--${lastNew.chip}`,
          )
        "
      >
        <div class="text-xs font-medium tracking-wide uppercase opacity-80">
          {{ lastNew.label }}
        </div>
        <div
          class="truncate text-right text-base font-semibold tabular-nums"
          :aria-label="lastNew.full"
          :title="lastNew.full"
        >
          {{ lastNew.value }}
        </div>
      </div>

      <div
        v-for="(pair, row) in pairs"
        :key="row"
        class="grid grid-cols-2 gap-1.5"
      >
        <div
          v-for="m in pair"
          :key="m.key"
          :class="
            cn(
              'flex min-h-14 min-w-0 flex-col justify-between rounded-lg px-2.5 py-2',
              `jr-stat-chip--${m.chip}`,
            )
          "
        >
          <div class="text-xs font-medium tracking-wide uppercase opacity-80">
            {{ m.label }}
          </div>
          <div
            class="truncate text-right text-base font-semibold tabular-nums"
            :aria-label="m.full"
            :title="m.full"
          >
            {{ m.value }}
          </div>
        </div>
      </div>
    </div>
  </article>
</template>
