<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import {
  ArrowDown,
  ArrowUp,
  CalendarPlus,
  Filter,
  Grid2x2,
  HardDrive,
  List,
  RefreshCw,
  RotateCcw,
} from '@lucide/vue'
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Collapsible,
  CollapsibleContent,
} from '@/components/ui/collapsible'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { Switch } from '@/components/ui/switch'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import JackettFiltersForm from '@/components/search/JackettFiltersForm.vue'
import NativeFiltersForm from '@/components/search/NativeFiltersForm.vue'
import { useToolbarDensity } from '@/composables/useToolbarDensity'
import { type ApiMode, type V2SearchFilters } from '@/lib/jackett'
import { segmentItemSort, segmentItem, segmentTrack, segmentTrackSort } from '@/lib/segment-classes'
import {
  SORT_OPTIONS,
  type SearchFilters,
  type SortDirection,
  type SortValue,
} from '@/lib/torrents'
import { cn } from '@/lib/utils'

const SORT_ICONS = {
  sid: ArrowUp,
  size: HardDrive,
  date: CalendarPlus,
  update: RefreshCw,
} as const

const props = defineProps<{
  open: boolean
  exact: boolean
  apiMode: ApiMode
  sort: SortValue
  sortDir: SortDirection
  listView: boolean
  filters: SearchFilters
  v2Filters: V2SearchFilters
  facets: {
    type: string[]
    tracker: string[]
    voice: string[]
    year: string[]
    quality: string[]
    season: string[]
    lang: string[]
  }
  activeCount: number
}>()
const emit = defineEmits<{
  'update:open': [boolean]
  'update:exact': [boolean]
  'update:apiMode': [ApiMode]
  'update:sort': [SortValue]
  'update:listView': [boolean]
  serverFilter: [key: keyof SearchFilters, value: string]
  v2Filter: [
    key: 'title' | 'titleOriginal' | 'year' | 'isSerial' | 'videotype',
    value: string,
  ]
  toggleCategory: [category: string]
  toggleList: [
    key: 'trackers' | 'qualities' | 'voices' | 'seasons' | 'langs',
    value: string,
  ]
  clientFilter: [key: 'refine' | 'exclude', value: string]
  reset: []
}>()

const { t, locale } = useI18n()
const isMobile = useMediaQuery('(max-width: 639px)')

const dockRef = ref<HTMLElement | null>(null)
const bandRef = ref<HTMLElement | null>(null)
const { measure: measureToolbarDensity } = useToolbarDensity({
  dockRef,
  bandRef,
})

watch(
  bandRef,
  (el) => {
    dockRef.value = (el?.closest('.jr-search-dock') as HTMLElement | null) ?? null
  },
  { immediate: true },
)

watch(locale, () => {
  requestAnimationFrame(() => measureToolbarDensity())
})

function sortAriaLabel(opt: (typeof SORT_OPTIONS)[number]) {
  const field = t(opt.labelKey)
  if (props.sort !== opt.value) {
    return t('search.sortDesc', { field })
  }
  return t(
    props.sortDir === 'asc' ? 'search.sortAsc' : 'search.sortDesc',
    { field },
  )
}

function onSortChipClick(value: SortValue) {
  emit('update:sort', value)
}

function setOpen(value: boolean) {
  emit('update:open', value)
}

function onServer(key: keyof SearchFilters, value: string) {
  emit('serverFilter', key, value)
}

function onV2Filter(
  key: 'title' | 'titleOriginal' | 'year' | 'isSerial' | 'videotype',
  value: string,
) {
  emit('v2Filter', key, value)
}

function onToggleCategory(value: string) {
  emit('toggleCategory', value)
}

function onToggleList(
  key: 'trackers' | 'qualities' | 'voices' | 'seasons' | 'langs',
  value: string,
) {
  emit('toggleList', key, value)
}

function onClientFilter(key: 'refine' | 'exclude', value: string) {
  emit('clientFilter', key, value)
}
</script>

<template>
  <div class="space-y-2.5">
    <TooltipProvider :delay-duration="400">
      <div class="jr-search-toolbar">
        <!-- Band 1: sort — full width, labels always -->
        <div class="jr-toolbar-band jr-toolbar-band--sort">
          <ToggleGroup
            type="single"
            :model-value="sort"
            size="sm"
            :spacing="1"
            :class="cn(segmentTrackSort, 'justify-stretch')"
            :aria-label="t('search.sortMode')"
          >
            <ToggleGroupItem
              v-for="opt in SORT_OPTIONS"
              :key="opt.value"
              :value="opt.value"
              :class="segmentItemSort"
              :aria-label="sortAriaLabel(opt)"
              :aria-sort="
                sort === opt.value
                  ? sortDir === 'asc'
                    ? 'ascending'
                    : 'descending'
                  : undefined
              "
              @click="onSortChipClick(opt.value)"
            >
              <component
                :is="SORT_ICONS[opt.value]"
                class="size-3.5 shrink-0"
                aria-hidden="true"
              />
              <span class="jr-toolbar-label jr-toolbar-label--sort">{{
                t(opt.labelKey)
              }}</span>
              <component
                :is="sortDir === 'asc' ? ArrowUp : ArrowDown"
                v-if="sort === opt.value"
                class="size-3 shrink-0 opacity-80"
                aria-hidden="true"
              />
            </ToggleGroupItem>
          </ToggleGroup>
        </div>

        <!-- Band 2: mode · exact · spacer · actions · view -->
        <div ref="bandRef" class="jr-toolbar-band jr-toolbar-band--controls">
          <div class="jr-toolbar-cluster">
            <ToggleGroup
              type="single"
              :model-value="apiMode"
              size="sm"
              :spacing="1"
              :class="segmentTrack"
              :aria-label="t('search.apiMode.label')"
              @update:model-value="(v) => v && emit('update:apiMode', v as ApiMode)"
            >
              <ToggleGroupItem value="v1" :class="segmentItem">
                {{ t('search.apiMode.native') }}
              </ToggleGroupItem>
              <ToggleGroupItem value="v2" :class="segmentItem">
                {{ t('search.apiMode.jackett') }}
              </ToggleGroupItem>
            </ToggleGroup>

            <span class="jr-toolbar-sep" aria-hidden="true" />

            <Tooltip v-if="apiMode === 'v1'">
              <TooltipTrigger as-child>
                <label for="exact-search" class="jr-exact-toggle">
                  <Switch
                    id="exact-search"
                    :model-value="exact"
                    :aria-label="t('search.filters.exact')"
                    @update:model-value="(v) => emit('update:exact', !!v)"
                  />
                  <span class="jr-toolbar-label jr-toolbar-label--exact">{{
                    t('search.filters.exact')
                  }}</span>
                </label>
              </TooltipTrigger>
              <TooltipContent>{{ t('search.filters.exact') }}</TooltipContent>
            </Tooltip>
          </div>

          <div class="jr-toolbar-spacer" aria-hidden="true" />

          <div class="jr-toolbar-actions">
            <Tooltip>
              <TooltipTrigger as-child>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  class="jr-toolbar-btn"
                  :disabled="!activeCount"
                  :aria-label="t('search.filters.reset')"
                  @click="emit('reset')"
                >
                  <RotateCcw class="size-3.5 shrink-0" />
                  <span class="jr-toolbar-label jr-toolbar-label--secondary">{{
                    t('search.filters.reset')
                  }}</span>
                </Button>
              </TooltipTrigger>
              <TooltipContent>{{ t('search.filters.reset') }}</TooltipContent>
            </Tooltip>

            <Tooltip>
              <TooltipTrigger as-child>
                <Button
                  id="search-filters-trigger"
                  type="button"
                  variant="ghost"
                  size="sm"
                  :class="
                    cn('jr-toolbar-btn', open && 'jr-toolbar-btn--on')
                  "
                  :aria-label="t('search.filters.filters')"
                  :aria-expanded="open"
                  aria-controls="search-filters-panel"
                  @click="setOpen(!open)"
                >
                  <Filter class="size-3.5" aria-hidden="true" />
                  <span
                    class="jr-toolbar-label jr-toolbar-label--secondary"
                    aria-hidden="true"
                    >{{ t('search.filters.filters') }}</span
                  >
                  <Badge
                    v-if="activeCount"
                    variant="secondary"
                    class="ml-0.5 size-5 justify-center rounded-full bg-primary p-0 text-[10px] text-primary-foreground"
                  >
                    {{ activeCount }}
                  </Badge>
                </Button>
              </TooltipTrigger>
              <TooltipContent>{{ t('search.filters.filters') }}</TooltipContent>
            </Tooltip>
          </div>

          <ToggleGroup
            type="single"
            :model-value="listView ? 'list' : 'cards'"
            size="sm"
            :spacing="1"
            :class="cn(segmentTrack, 'jr-toolbar-view')"
            :aria-label="t('search.viewMode')"
            @update:model-value="
              (v) => v && emit('update:listView', v === 'list')
            "
          >
            <ToggleGroupItem
              value="list"
              :class="segmentItem"
              :aria-label="t('search.list')"
            >
              <List class="size-3.5" />
              <span class="jr-toolbar-label jr-toolbar-label--secondary">{{
                t('search.list')
              }}</span>
            </ToggleGroupItem>
            <ToggleGroupItem
              value="cards"
              :class="segmentItem"
              :aria-label="t('search.cards')"
            >
              <Grid2x2 class="size-3.5" />
              <span class="jr-toolbar-label jr-toolbar-label--secondary">{{
                t('search.cards')
              }}</span>
            </ToggleGroupItem>
          </ToggleGroup>
        </div>
      </div>
    </TooltipProvider>

    <!-- Mobile: Reka Sheet bottom drawer -->
    <Sheet
      v-if="isMobile"
      :open="open"
      @update:open="setOpen"
    >
      <SheetContent
        side="bottom"
        class="max-h-[85dvh] rounded-t-2xl p-0"
      >
        <SheetHeader
          class="jr-sheet-drag-zone shrink-0 space-y-0 border-b p-0 px-4 pt-1 pb-3 pr-12 text-left"
        >
          <SheetTitle>{{ t('search.filters.filters') }}</SheetTitle>
          <SheetDescription class="sr-only">
            {{ t('search.filters.panel') }}
          </SheetDescription>
        </SheetHeader>

        <div
          id="search-filters-panel"
          role="region"
          :aria-label="t('search.filters.panel')"
          class="jr-sheet-body min-h-0 flex-1 space-y-3 overflow-y-auto overscroll-contain px-4 py-3"
        >
          <JackettFiltersForm
            v-if="apiMode === 'v2'"
            :filters="v2Filters"
            :facets="facets"
            mobile-select
            @v2-filter="onV2Filter"
            @toggle-category="onToggleCategory"
            @toggle-list="onToggleList"
            @client-filter="onClientFilter"
          />
          <NativeFiltersForm
            v-else
            :filters="filters"
            :facets="facets"
            mobile-select
            @server-filter="onServer"
            @client-filter="onClientFilter"
          />
        </div>

        <div class="shrink-0 border-t px-4 pt-3 pb-3">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            class="gap-1.5"
            :disabled="!activeCount"
            @click="emit('reset')"
          >
            <RotateCcw class="size-3.5" />
            {{ t('search.filters.reset') }}
          </Button>
        </div>
      </SheetContent>
    </Sheet>

    <!-- Desktop filters panel -->
    <Collapsible
      v-else
      :open="open"
      @update:open="setOpen"
    >
      <CollapsibleContent>
        <div
          id="search-filters-panel"
          role="region"
          :aria-label="t('search.filters.panel')"
          class="jr-filters-panel"
        >
          <JackettFiltersForm
            v-if="apiMode === 'v2'"
            :filters="v2Filters"
            :facets="facets"
            @v2-filter="onV2Filter"
            @toggle-category="onToggleCategory"
            @toggle-list="onToggleList"
            @client-filter="onClientFilter"
          />
          <NativeFiltersForm
            v-else
            :filters="filters"
            :facets="facets"
            @server-filter="onServer"
            @client-filter="onClientFilter"
          />
        </div>
      </CollapsibleContent>
    </Collapsible>
  </div>
</template>
