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

    <div class="flex flex-col gap-2 lg:flex-row lg:items-center">
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

      <div class="flex flex-wrap items-center gap-2">
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
          variant="outline"
          size="sm"
          class="h-9"
          :aria-label="t('stats.viewMode')"
          @update:model-value="(v) => v && setView(v as 'table' | 'cards')"
        >
          <ToggleGroupItem value="table" class="gap-1.5 px-2.5">
            <Table2 class="size-3.5" />
            <span class="hidden sm:inline">{{ t('stats.table') }}</span>
          </ToggleGroupItem>
          <ToggleGroupItem value="cards" class="gap-1.5 px-2.5">
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
      <div class="h-28 animate-pulse rounded-xl bg-muted/70" />
      <div class="h-64 animate-pulse rounded-xl bg-muted/70" />
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
