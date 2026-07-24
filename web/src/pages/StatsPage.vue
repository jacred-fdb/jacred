<script setup lang="ts">
import type { ComponentPublicInstance } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Clock3,
  Ellipsis,
  Hash,
  LayoutGrid,
  Loader2,
  Maximize2,
  Minimize2,
  RefreshCw,
  Search,
  Table2,
} from '@lucide/vue'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import StatsCard from '@/components/stats/StatsCard.vue'
import StatsSummary from '@/components/stats/StatsSummary.vue'
import StatsTable from '@/components/stats/StatsTable.vue'
import { useStats } from '@/composables/useStats'
import { type StatsSort } from '@/lib/stats'
import { cn } from '@/lib/utils'

const { t } = useI18n()

const sortOptions = [
  { value: 'name' as const, labelKey: 'stats.sortName' },
  { value: 'newtor' as const, labelKey: 'stats.sortNew' },
  { value: 'update' as const, labelKey: 'stats.sortUpdate' },
  { value: 'alltorrents' as const, labelKey: 'stats.sortAll' },
  { value: 'confirm' as const, labelKey: 'stats.sortConfirm' },
  { value: 'wait' as const, labelKey: 'stats.sortWait' },
  { value: 'skip' as const, labelKey: 'stats.sortSkip' },
] as const

const {
  query,
  sort,
  view,
  fullNumbers,
  wideMode,
  page,
  isLoading,
  errorMessage,
  gridEl,
  filtered,
  aggregate,
  pagedRows,
  paginationLabel,
  totalPages,
  showTable,
  showMobileList,
  counterLabel,
  updatedLabel,
  setQuery,
  setSort,
  setView,
  toggleNumbers,
  toggleWide,
  prevPage,
  nextPage,
  load,
} = useStats()

function bindGridEl(el: Element | ComponentPublicInstance | null) {
  gridEl.value = el instanceof HTMLElement ? el : null
}
</script>

<template>
  <section class="space-y-4">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div class="space-y-1">
        <h1 class="text-2xl font-semibold tracking-tight">
          {{ t('stats.title') }}
        </h1>
        <p
          v-if="updatedLabel"
          class="inline-flex items-center gap-1.5 text-sm text-muted-foreground"
          :title="t('stats.lastCollected')"
        >
          <Clock3 class="size-3.5" />
          {{ updatedLabel }}
        </p>
      </div>
      <Button
        type="button"
        variant="outline"
        size="sm"
        class="h-9 gap-1.5"
        :disabled="isLoading"
        :aria-busy="isLoading"
        @click="load"
      >
        <Loader2 v-if="isLoading" class="size-3.5 animate-spin" />
        <RefreshCw v-else class="size-3.5" />
        {{ t('stats.refresh') }}
      </Button>
    </div>

    <div
      class="jr-stats-dock sticky z-20 flex flex-col gap-2 bg-background py-2 lg:flex-row lg:items-center"
      style="top: var(--jr-header-offset)"
    >
      <div class="relative min-w-0 flex-1">
        <Search
          class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
        />
        <Input
          id="stats-search"
          :model-value="query"
          type="search"
          class="h-9 pl-9"
          :placeholder="t('stats.searchTracker')"
          :aria-label="t('stats.searchAria')"
          @update:model-value="(v) => setQuery(String(v))"
        />
      </div>

      <div class="flex flex-wrap items-center gap-2 lg:shrink-0">
        <Select
          :model-value="sort"
          @update:model-value="(v) => setSort(String(v) as StatsSort)"
        >
          <SelectTrigger class="h-9 w-full sm:w-44" :aria-label="t('stats.sort')">
            <SelectValue :placeholder="t('stats.sort')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem
              v-for="opt in sortOptions"
              :key="opt.value"
              :value="opt.value"
            >
              {{ t(opt.labelKey) }}
            </SelectItem>
          </SelectContent>
        </Select>

        <ToggleGroup
          type="single"
          :model-value="view"
          size="sm"
          :spacing="1"
          class="h-9 w-max max-w-full rounded-[10px] bg-secondary p-0.5"
          :aria-label="t('stats.viewMode')"
          @update:model-value="(v) => v && setView(v as 'table' | 'cards')"
        >
          <ToggleGroupItem
            value="table"
            class="!rounded-[8px] gap-1.5 border-0 px-2.5 shadow-none data-[state=on]:bg-background data-[state=on]:text-foreground data-[state=on]:shadow-[0_1px_2px_rgba(0,0,0,0.28)]"
          >
            <Table2 class="size-3.5" />
            <span class="hidden sm:inline">{{ t('stats.table') }}</span>
          </ToggleGroupItem>
          <ToggleGroupItem
            value="cards"
            class="!rounded-[8px] gap-1.5 border-0 px-2.5 shadow-none data-[state=on]:bg-background data-[state=on]:text-foreground data-[state=on]:shadow-[0_1px_2px_rgba(0,0,0,0.28)]"
          >
            <LayoutGrid class="size-3.5" />
            <span class="hidden sm:inline">{{
              showMobileList ? t('stats.list') : t('stats.cards')
            }}</span>
          </ToggleGroupItem>
        </ToggleGroup>

        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button
              type="button"
              variant="outline"
              size="icon"
              class="size-9"
              :aria-label="t('stats.moreOptions')"
            >
              <Ellipsis class="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem @select="toggleNumbers">
              <Hash />
              {{ fullNumbers ? t('stats.shortNumbers') : t('stats.fullNumbers') }}
            </DropdownMenuItem>
            <DropdownMenuItem
              v-if="view === 'cards'"
              @select="toggleWide"
            >
              <Minimize2 v-if="wideMode" />
              <Maximize2 v-else />
              {{ wideMode ? t('stats.narrow') : t('stats.wide') }}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>

        <span class="text-sm text-muted-foreground lg:ml-1">
          {{ counterLabel }}
        </span>
      </div>
    </div>

    <p
      v-if="errorMessage"
      class="rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
      role="alert"
    >
      {{ errorMessage }}
    </p>

    <div
      v-if="isLoading"
      class="space-y-3"
      aria-busy="true"
      :aria-label="t('stats.loading')"
    >
      <div class="jr-glass animate-pulse rounded-xl border p-4">
        <div class="mb-3 h-4 w-40 rounded bg-muted-foreground/20" />
        <div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <div
            v-for="i in 3"
            :key="`p-${i}`"
            class="h-16 rounded-lg bg-muted-foreground/15"
          />
        </div>
        <div class="mt-3 grid grid-cols-1 gap-1.5 border-t border-border/60 pt-3 sm:grid-cols-3">
          <div
            v-for="i in 3"
            :key="`s-${i}`"
            class="h-9 rounded-md bg-muted-foreground/10"
          />
        </div>
      </div>
      <div
        v-if="view === 'table'"
        class="animate-pulse overflow-hidden rounded-xl border"
      >
        <div class="h-10 border-b bg-muted/50" />
        <div
          v-for="i in 8"
          :key="i"
          class="flex h-11 items-center gap-3 border-b border-border/40 px-3 last:border-0"
        >
          <div class="size-5 shrink-0 rounded-sm bg-muted-foreground/20" />
          <div class="h-3.5 w-28 rounded bg-muted-foreground/20" />
          <div class="ml-auto h-3.5 w-16 rounded bg-muted-foreground/15" />
          <div class="h-3.5 w-12 rounded bg-muted-foreground/15" />
          <div class="h-3.5 w-12 rounded bg-muted-foreground/15" />
        </div>
      </div>
      <div
        v-else
        class="grid grid-cols-1 gap-3 sm:grid-cols-2"
      >
        <div
          v-for="i in 4"
          :key="i"
          class="jr-glass animate-pulse space-y-3 rounded-xl border p-4"
        >
          <div class="flex items-center gap-2.5">
            <div class="size-5 rounded-sm bg-muted-foreground/20" />
            <div class="h-4 w-32 rounded bg-muted-foreground/20" />
          </div>
          <div class="h-14 rounded-lg bg-muted-foreground/15" />
          <div class="grid grid-cols-2 gap-1.5">
            <div class="h-14 rounded-lg bg-muted-foreground/10" />
            <div class="h-14 rounded-lg bg-muted-foreground/10" />
          </div>
        </div>
      </div>
    </div>

    <template v-else>
      <div
        v-if="!filtered.length"
        class="jr-glass-panel rounded-xl border border-dashed px-4 py-12 text-center text-muted-foreground"
      >
        {{ t('stats.nothingFound') }}
      </div>

      <template v-else>
        <StatsSummary :aggregate="aggregate" :full-numbers="fullNumbers" />

        <StatsTable
          v-if="showTable"
          :rows="pagedRows"
          :sort="sort"
          :full-numbers="fullNumbers"
          @sort="setSort"
        />

        <div
          v-else
          :ref="bindGridEl"
          :class="
            cn(
              'grid gap-3',
              showMobileList
                ? 'grid-cols-1'
                : wideMode
                  ? 'sm:grid-cols-2 xl:grid-cols-3'
                  : 'sm:grid-cols-1 md:grid-cols-2',
            )
          "
        >
          <StatsCard
            v-for="item in pagedRows"
            :key="item.trackerName"
            :item="item"
            :full-numbers="fullNumbers"
            :compact="showMobileList"
          />
        </div>

        <div
          v-if="paginationLabel"
          class="flex flex-wrap items-center justify-between gap-2"
        >
          <p class="text-sm text-muted-foreground">{{ paginationLabel }}</p>
          <div class="flex gap-2">
            <Button
              type="button"
              size="sm"
              variant="outline"
              class="h-9"
              :disabled="page <= 1"
              @click="prevPage"
            >
              {{ t('stats.prev') }}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              class="h-9"
              :disabled="page >= totalPages"
              @click="nextPage"
            >
              {{ t('stats.next') }}
            </Button>
          </div>
        </div>
      </template>
    </template>
  </section>
</template>
