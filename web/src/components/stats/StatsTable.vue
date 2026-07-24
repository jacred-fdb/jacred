<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ArrowDown, ArrowUp } from '@lucide/vue'
import {
  formatStatNumber,
  formatStatNumberFull,
  getTrackerDisplayName,
  getTracksData,
  type StatsSort,
  type TrackerStat,
} from '@/lib/stats'
import { getSafeIconPath } from '@/lib/torrents'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

defineProps<{
  rows: TrackerStat[]
  sort: StatsSort
  fullNumbers: boolean
}>()

const emit = defineEmits<{
  sort: [StatsSort]
}>()

const { t, locale } = useI18n()

const columns = computed(() => [
  {
    key: 'name' as const,
    label: t('stats.colTracker'),
    sortKey: 'name' as StatsSort,
    class: 'min-w-[10rem]',
    sticky: true,
  },
  {
    key: 'lastnew' as const,
    label: t('stats.colLastNew'),
    class: 'whitespace-nowrap',
    tone: 'text-[var(--jr-stat-lastnew)]',
  },
  {
    key: 'newtor' as const,
    label: t('stats.sortNew'),
    sortKey: 'newtor' as StatsSort,
    tone: 'text-[var(--jr-stat-new)]',
    numeric: true,
  },
  {
    key: 'update' as const,
    label: t('stats.sortUpdate'),
    sortKey: 'update' as StatsSort,
    tone: 'text-[var(--jr-stat-update)]',
    numeric: true,
  },
  {
    key: 'alltorrents' as const,
    label: t('stats.sortAll'),
    sortKey: 'alltorrents' as StatsSort,
    tone: 'text-[var(--jr-stat-total)]',
    numeric: true,
  },
  {
    key: 'confirm' as const,
    label: t('stats.sortConfirm'),
    sortKey: 'confirm' as StatsSort,
    tone: 'text-[var(--jr-stat-confirm)]',
    numeric: true,
  },
  {
    key: 'wait' as const,
    label: t('stats.sortWait'),
    sortKey: 'wait' as StatsSort,
    tone: 'text-[var(--jr-stat-wait)]',
    numeric: true,
  },
  {
    key: 'skip' as const,
    label: t('stats.sortSkip'),
    sortKey: 'skip' as StatsSort,
    tone: 'text-[var(--jr-stat-skip)]',
    numeric: true,
  },
])

function ariaSort(
  col: (typeof columns.value)[number],
  sort: StatsSort,
) {
  if (!col.sortKey || col.sortKey !== sort) return undefined
  return sort === 'name' ? 'ascending' : 'descending'
}

function headerClass(col: (typeof columns.value)[number]) {
  return cn(
    col.tone || 'text-muted-foreground',
    col.numeric && 'text-right',
  )
}

function cellClass(key: string, sort: StatsSort) {
  const tone = columns.value.find((c) => c.key === key)?.tone
  if (tone) return cn(tone, 'font-semibold')
  if (sort === key) return 'font-medium text-foreground'
  return 'font-medium text-foreground/80'
}
</script>

<template>
  <div class="jr-glass overflow-x-auto rounded-xl border">
    <p class="border-b border-border/60 px-3 py-2 text-xs text-muted-foreground sm:hidden">
      {{ t('stats.scrollHint') }}
    </p>
    <table class="w-full min-w-[48rem] border-collapse text-sm">
      <thead>
        <tr
          class="border-b border-border text-left"
          style="background: var(--jr-stats-thead-bg)"
        >
          <th
            v-for="col in columns"
            :key="col.key"
            scope="col"
            :aria-sort="ariaSort(col, sort)"
            :class="
              cn(
                'px-3 py-2.5 font-medium whitespace-nowrap',
                headerClass(col),
                col.class,
                col.numeric && 'min-w-[5.5rem]',
                col.sticky &&
                  'sticky left-0 z-10 bg-[var(--jr-stats-thead-bg)] shadow-[1px_0_0_var(--border)]',
              )
            "
          >
            <button
              v-if="col.sortKey"
              type="button"
              :class="
                cn(
                  'inline-flex items-center gap-1 hover:opacity-90',
                  col.numeric && 'w-full justify-end',
                )
              "
              @click="emit('sort', col.sortKey!)"
            >
              {{ col.label }}
              <ArrowUp
                v-if="col.sortKey === sort && sort === 'name'"
                class="size-3 opacity-80"
                aria-hidden="true"
              />
              <ArrowDown
                v-else-if="col.sortKey === sort"
                class="size-3 opacity-80"
                aria-hidden="true"
              />
            </button>
            <span v-else>{{ col.label }}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="item in rows"
          :key="item.trackerName"
          class="border-b border-border/60 last:border-0 hover:bg-muted/30"
        >
          <td
            class="sticky left-0 z-10 bg-background px-3 py-2.5 shadow-[1px_0_0_var(--border)]"
          >
            <Tooltip>
              <TooltipTrigger as-child>
                <div class="flex items-center gap-2">
                  <img
                    :src="getSafeIconPath(item.trackerName)"
                    alt=""
                    width="16"
                    height="16"
                    class="size-4 rounded-sm"
                    loading="lazy"
                    @error="
                      ($event.target as HTMLImageElement).style.visibility =
                        'hidden'
                    "
                  />
                  <div class="min-w-0 truncate font-medium">
                    {{ getTrackerDisplayName(item.trackerName) }}
                  </div>
                </div>
              </TooltipTrigger>
              <TooltipContent>
                {{ (item.trackerName || '').toLowerCase() }}
              </TooltipContent>
            </Tooltip>
          </td>
          <td
            :class="
              cn(
                'px-3 py-2.5 whitespace-nowrap tabular-nums font-medium',
                cellClass('lastnew', sort),
              )
            "
          >
            {{ item.lastnewtor || '—' }}
          </td>
          <td
            :class="cn('px-3 py-2.5 text-right tabular-nums', cellClass('newtor', sort))"
            :aria-label="formatStatNumberFull(item.newtor, locale)"
            :title="formatStatNumberFull(item.newtor, locale)"
          >
            {{ formatStatNumber(item.newtor, fullNumbers, locale) }}
          </td>
          <td
            :class="cn('px-3 py-2.5 text-right tabular-nums', cellClass('update', sort))"
            :aria-label="formatStatNumberFull(item.update, locale)"
            :title="formatStatNumberFull(item.update, locale)"
          >
            {{ formatStatNumber(item.update, fullNumbers, locale) }}
          </td>
          <td
            :class="
              cn('px-3 py-2.5 text-right tabular-nums', cellClass('alltorrents', sort))
            "
            :aria-label="formatStatNumberFull(item.alltorrents, locale)"
            :title="formatStatNumberFull(item.alltorrents, locale)"
          >
            {{ formatStatNumber(item.alltorrents, fullNumbers, locale) }}
          </td>
          <td
            :class="cn('px-3 py-2.5 text-right tabular-nums', cellClass('confirm', sort))"
            :aria-label="formatStatNumberFull(getTracksData(item).confirm, locale)"
            :title="formatStatNumberFull(getTracksData(item).confirm, locale)"
          >
            {{
              formatStatNumber(getTracksData(item).confirm, fullNumbers, locale)
            }}
          </td>
          <td
            :class="cn('px-3 py-2.5 text-right tabular-nums', cellClass('wait', sort))"
            :aria-label="formatStatNumberFull(getTracksData(item).wait, locale)"
            :title="formatStatNumberFull(getTracksData(item).wait, locale)"
          >
            {{ formatStatNumber(getTracksData(item).wait, fullNumbers, locale) }}
          </td>
          <td
            :class="cn('px-3 py-2.5 text-right tabular-nums', cellClass('skip', sort))"
            :aria-label="formatStatNumberFull(getTracksData(item).skip, locale)"
            :title="formatStatNumberFull(getTracksData(item).skip, locale)"
          >
            {{ formatStatNumber(getTracksData(item).skip, fullNumbers, locale) }}
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
