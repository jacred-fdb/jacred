<script setup lang="ts">
import { useMediaQuery, useOnline } from '@vueuse/core'
import type { ComponentPublicInstance } from 'vue'
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Loader2, Search, WifiOff, X } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import VirtualList from '@/components/VirtualList.vue'
import SearchFilters from '@/components/search/SearchFilters.vue'
import TorrentCard from '@/components/search/TorrentCard.vue'
import { useTorrents } from '@/composables/useTorrents'
import { useShellTools } from '@/composables/useShellTools'
import {
  clearRecentSearches,
  getRecentSearches,
} from '@/lib/recent-searches'
import { torrentKey } from '@/lib/torrents'
import { cn } from '@/lib/utils'

defineOptions({ name: 'SearchPage' })

const { t } = useI18n()
const { openTorrServer } = useShellTools()
const searchState = useTorrents()
const {
  query,
  sort,
  exact,
  listView,
  filtersOpen,
  filters,
  facets,
  visibleItems,
  isLoading,
  errorMessage,
  currentQuery,
  activeFilterCount,
  resultsHeader,
  search,
  prefetchRecent,
  setSort,
  setExact,
  toggleListView,
  setFiltersOpen,
  updateServerFilter,
  updateClientFilter,
  resetFilters,
  toggleTrackerFilter,
  clearSearch,
} = searchState
const resultsEl = searchState.resultsEl

const listRef = ref<{
  syncScrollMargin?: () => void
  getFirstVisibleIndex?: () => number
  scrollToIndex?: (
    index: number,
    align?: 'start' | 'center' | 'end' | 'auto',
  ) => void
} | null>(null)
const recent = ref(getRecentSearches())
const isSmUp = useMediaQuery('(min-width: 640px)')
const isOnline = useOnline()

const LIST_GAP = 4
const CARD_GAP = 10
const estimateSize = computed(() => {
  if (listView.value) return isSmUp.value ? 76 : 114
  return isSmUp.value ? 140 : 156
})

const hasResults = computed(
  () => !!currentQuery.value && visibleItems.value.length > 0,
)
const showEmptyHint = computed(
  () => !currentQuery.value && !visibleItems.value.length && !isLoading.value,
)
const showNothingFound = computed(
  () =>
    !!currentQuery.value &&
    !visibleItems.value.length &&
    !isLoading.value &&
    !errorMessage.value,
)

/** Pin viewport to document top so results start under the search dock. */
function pinResultsStart() {
  window.scrollTo(0, 0)
  document.documentElement.scrollTop = 0
  document.body.scrollTop = 0
}

async function settleListLayout() {
  pinResultsStart()
  await nextTick()
  pinResultsStart()
  await new Promise<void>((resolve) => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })
  pinResultsStart()
  listRef.value?.syncScrollMargin?.()
  // Late layout (virtual row measure / sticky dock) can re-trigger scroll anchoring.
  window.setTimeout(pinResultsStart, 50)
  window.setTimeout(pinResultsStart, 120)
}

function onSubmit(e: Event) {
  e.preventDefault()
  if (!isOnline.value) return
  // Blur submit so the browser does not scroll the focused control into view.
  if (document.activeElement instanceof HTMLElement) {
    document.activeElement.blur()
  }
  pinResultsStart()
  void search().then(async () => {
    recent.value = getRecentSearches()
    await settleListLayout()
  })
}

function applyRecent(q: string) {
  query.value = q
  if (document.activeElement instanceof HTMLElement) {
    document.activeElement.blur()
  }
  pinResultsStart()
  void search().then(async () => {
    recent.value = getRecentSearches()
    await settleListLayout()
  })
}

function onClearRecent() {
  clearRecentSearches()
  recent.value = []
}

/** Keep the same result under the fold when list ↔ cards row heights change. */
async function onListViewUpdate(next: boolean) {
  if (next === listView.value) return
  const anchorIndex = listRef.value?.getFirstVisibleIndex?.() ?? 0
  toggleListView()
  // VirtualList remounts on layout key — wait until the new instance is ready.
  await nextTick()
  await nextTick()
  await new Promise<void>((resolve) => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })
  listRef.value?.syncScrollMargin?.()
  listRef.value?.scrollToIndex?.(anchorIndex, 'start')
  // Late measureElement passes change totalSize — re-anchor once settled.
  window.setTimeout(() => {
    listRef.value?.scrollToIndex?.(anchorIndex, 'start')
  }, 50)
  window.setTimeout(() => {
    listRef.value?.scrollToIndex?.(anchorIndex, 'start')
  }, 120)
}

watch(isLoading, (loading) => {
  if (loading) pinResultsStart()
})

watch(hasResults, (ready) => {
  if (ready) void settleListLayout()
})

watch(filtersOpen, () => {
  void nextTick(() => listRef.value?.syncScrollMargin?.())
})

function bindResultsEl(
  el: Element | ComponentPublicInstance | null,
) {
  if (!el) {
    resultsEl.value = null
    return
  }
  if (el instanceof HTMLElement) {
    resultsEl.value = el
    return
  }
  const root = (el as ComponentPublicInstance).$el
  resultsEl.value = root instanceof HTMLElement ? root : null
}
</script>

<template>
  <section class="flex flex-col gap-4">
    <header class="space-y-1">
      <h1 class="text-2xl font-semibold tracking-tight text-balance sm:text-[1.75rem]">
        {{ t('search.title') }}
      </h1>
      <p class="max-w-2xl text-sm text-pretty text-muted-foreground">
        {{ t('search.subtitle') }}
      </p>
    </header>

    <div
      v-if="!isOnline"
      class="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
      role="status"
    >
      <WifiOff class="size-4 shrink-0" aria-hidden="true" />
      {{ t('search.offline') }}
    </div>

    <!-- Same chrome before and after search — sticky without a sudden “card” skin -->
    <div
      class="jr-search-dock sticky z-20 flex flex-col gap-2 bg-background/90 py-1 backdrop-blur-md sm:gap-3"
      style="top: var(--jr-header-offset)"
    >
      <form
        class="flex flex-col gap-2 sm:flex-row sm:items-stretch"
        @submit="onSubmit"
      >
        <div class="relative min-w-0 flex-1">
          <Search
            class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden="true"
          />
          <Input
            id="search-input"
            v-model="query"
            type="search"
            name="s"
            autocomplete="off"
            enterkeyhint="search"
            class="h-11 pr-10 pl-9"
            :placeholder="t('search.placeholder')"
            :aria-label="t('search.queryAria')"
          />
          <Button
            v-if="query"
            type="button"
            variant="ghost"
            size="icon"
            class="absolute top-1/2 right-1 size-8 -translate-y-1/2"
            :aria-label="t('search.clear')"
            @click="clearSearch"
          >
            <X class="size-4" />
          </Button>
        </div>
        <Button
          type="submit"
          class="h-11 shrink-0 gap-2 px-5 sm:min-w-[7.5rem]"
          :disabled="isLoading || !isOnline"
          :aria-busy="isLoading"
        >
          <Loader2 v-if="isLoading" class="size-4 animate-spin" />
          {{ isLoading ? t('search.searching') : t('search.submit') }}
        </Button>
      </form>

      <div
        v-if="recent.length && !currentQuery"
        class="flex flex-wrap items-center gap-2"
      >
        <span class="text-xs text-muted-foreground">{{ t('search.recent') }}</span>
        <Button
          v-for="item in recent"
          :key="item"
          type="button"
          variant="secondary"
          size="sm"
          class="h-7 max-w-[14rem] truncate px-2.5 text-xs font-normal"
          @mouseenter="prefetchRecent(item)"
          @focus="prefetchRecent(item)"
          @click="applyRecent(item)"
        >
          {{ item }}
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          class="h-7 px-2 text-xs text-muted-foreground"
          @click="onClearRecent"
        >
          {{ t('search.clearRecent') }}
        </Button>
      </div>

      <SearchFilters
        :open="filtersOpen"
        :exact="exact"
        :sort="sort"
        :list-view="listView"
        :filters="filters"
        :facets="facets"
        :active-count="activeFilterCount"
        @update:open="setFiltersOpen"
        @update:exact="setExact"
        @update:sort="setSort"
        @update:list-view="onListViewUpdate"
        @server-filter="(k, v) => updateServerFilter(k, v)"
        @client-filter="(k, v) => updateClientFilter(k, v)"
        @reset="resetFilters"
      />
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
      class="flex flex-col gap-2"
      aria-busy="true"
      :aria-label="t('search.loadingResults')"
    >
      <div
        v-for="i in 6"
        :key="i"
        :class="
          cn(
            'animate-pulse bg-muted/70',
            listView
              ? cn('rounded-md', isSmUp ? 'h-12' : 'h-[7.15rem]')
              : cn('rounded-lg', isSmUp ? 'h-[7.25rem]' : 'h-[9.75rem]'),
          )
        "
      />
    </div>

    <template v-else>
      <p
        v-if="resultsHeader"
        class="text-sm text-muted-foreground"
      >
        {{ resultsHeader }}
      </p>

      <div
        v-if="showEmptyHint"
        class="jr-glass-panel rounded-xl border border-dashed px-4 py-14 text-center text-muted-foreground"
      >
        {{ t('search.emptyHint') }}
      </div>

      <div
        v-else-if="showNothingFound"
        class="jr-glass-panel rounded-xl border border-dashed px-4 py-14 text-center text-muted-foreground"
      >
        {{ t('search.nothingFound') }}
      </div>

      <div
        v-else-if="hasResults"
        :ref="bindResultsEl"
        class="jr-results-list"
        aria-live="polite"
      >
        <VirtualList
          :key="`${currentQuery || 'empty'}:${listView ? 'list' : 'cards'}`"
          ref="listRef"
          :items="visibleItems"
          :estimate-size="estimateSize"
          :gap="listView ? LIST_GAP : CARD_GAP"
        >
          <template #default="{ item, index }">
            <TorrentCard
              :key="torrentKey(item)"
              :item="item"
              :list-view="listView"
              :position="index + 1"
              :set-size="visibleItems.length"
              :active-tracker="filters.tracker"
              @filter-tracker="toggleTrackerFilter"
              @open-torr-server="openTorrServer"
            />
          </template>
        </VirtualList>
      </div>
    </template>
  </section>
</template>
