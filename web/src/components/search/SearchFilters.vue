<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import {
  ArrowUp,
  CalendarPlus,
  Filter,
  Grid2x2,
  HardDrive,
  List,
  RefreshCw,
  RotateCcw,
} from '@lucide/vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
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
  SORT_OPTIONS,
  type SearchFilters,
  type SortValue,
} from '@/lib/torrents'

const SORT_ICONS = {
  sid: ArrowUp,
  size: HardDrive,
  date: CalendarPlus,
  update: RefreshCw,
} as const

defineProps<{
  open: boolean
  exact: boolean
  sort: SortValue
  listView: boolean
  filters: SearchFilters
  facets: {
    type: string[]
    tracker: string[]
    voice: string[]
    year: string[]
    quality: string[]
    season: string[]
  }
  activeCount: number
}>()

const emit = defineEmits<{
  'update:open': [boolean]
  'update:exact': [boolean]
  'update:sort': [SortValue]
  'update:listView': [boolean]
  serverFilter: [key: keyof SearchFilters, value: string]
  clientFilter: [key: 'refine' | 'exclude', value: string]
  reset: []
}>()

const { t } = useI18n()
const isMobile = useMediaQuery('(max-width: 639px)')

function onServer(key: keyof SearchFilters, value: string | undefined) {
  emit('serverFilter', key, value === '__all__' ? '' : (value ?? ''))
}

function selectModel(value: string) {
  return value || '__all__'
}

function setOpen(value: boolean) {
  emit('update:open', value)
}
</script>

<template>
  <div class="space-y-2">
    <div class="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
      <ToggleGroup
        type="single"
        :model-value="sort"
        variant="outline"
        size="sm"
        class="w-full justify-start sm:w-auto"
        @update:model-value="(v) => v && emit('update:sort', v as SortValue)"
      >
        <ToggleGroupItem
          v-for="opt in SORT_OPTIONS"
          :key="opt.value"
          :value="opt.value"
          class="gap-1.5 px-2.5 text-xs sm:text-sm"
        >
          <component
            :is="SORT_ICONS[opt.value]"
            class="size-3.5 shrink-0"
            aria-hidden="true"
          />
          {{ t(opt.labelKey) }}
        </ToggleGroupItem>
      </ToggleGroup>

      <div class="flex flex-wrap items-center gap-2 sm:ml-auto">
        <div
          class="flex h-8 items-center gap-2 rounded-md border border-[var(--jr-glass-border)] jr-glass-inset px-2.5"
        >
          <Switch
            id="exact-search"
            :model-value="exact"
            @update:model-value="(v) => emit('update:exact', !!v)"
          />
          <label for="exact-search" class="cursor-pointer text-sm leading-none">
            {{ t('search.filters.exact') }}
          </label>
        </div>

        <div class="flex h-8 items-center gap-1.5">
          <Button
            id="search-filters-trigger"
            type="button"
            variant="outline"
            size="sm"
            class="h-8 gap-1.5"
            :aria-expanded="open"
            aria-controls="search-filters-panel"
            @click="setOpen(!open)"
          >
            <Filter class="size-3.5" />
            <span class="hidden sm:inline">{{ t('search.filters.filters') }}</span>
            <Badge
              v-if="activeCount"
              variant="secondary"
              class="ml-0.5 size-5 justify-center rounded-full p-0 text-[10px]"
            >
              {{ activeCount }}
            </Badge>
          </Button>

          <ToggleGroup
            type="single"
            :model-value="listView ? 'list' : 'cards'"
            variant="outline"
            size="sm"
            class="justify-start"
            :aria-label="t('search.viewMode')"
            @update:model-value="
              (v) => v && emit('update:listView', v === 'list')
            "
          >
            <ToggleGroupItem value="list" class="gap-1.5 px-2.5 text-xs sm:text-sm">
              <List class="size-3.5" />
              <span class="hidden sm:inline">{{ t('search.list') }}</span>
            </ToggleGroupItem>
            <ToggleGroupItem value="cards" class="gap-1.5 px-2.5 text-xs sm:text-sm">
              <Grid2x2 class="size-3.5" />
              <span class="hidden sm:inline">{{ t('search.cards') }}</span>
            </ToggleGroupItem>
          </ToggleGroup>
        </div>
      </div>
    </div>

    <!-- Mobile: Reka Sheet bottom drawer -->
    <Sheet
      v-if="isMobile"
      :open="open"
      @update:open="setOpen"
    >
      <SheetContent
        side="bottom"
        class="max-h-[85dvh] overflow-y-auto rounded-t-2xl"
      >
        <SheetHeader>
          <SheetTitle>{{ t('search.filters.filters') }}</SheetTitle>
          <SheetDescription>{{ t('search.filters.panel') }}</SheetDescription>
        </SheetHeader>
        <div
          id="search-filters-panel"
          class="mt-4 grid gap-3 pb-[env(safe-area-inset-bottom)]"
        >
            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.type') }}
              <Select
                :model-value="selectModel(filters.type)"
                @update:model-value="(v) => onServer('type', String(v))"
              >
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="t('search.filters.all')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                  <SelectItem v-for="v in facets.type" :key="v" :value="v">{{ v }}</SelectItem>
                </SelectContent>
              </Select>
            </label>

            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.tracker') }}
              <Select
                :model-value="selectModel(filters.tracker)"
                @update:model-value="(v) => onServer('tracker', String(v))"
              >
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="t('search.filters.all')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                  <SelectItem v-for="v in facets.tracker" :key="v" :value="v">{{ v }}</SelectItem>
                </SelectContent>
              </Select>
            </label>

            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.voice') }}
              <Select
                :model-value="selectModel(filters.voice)"
                @update:model-value="(v) => onServer('voice', String(v))"
              >
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="t('search.filters.all')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                  <SelectItem v-for="v in facets.voice" :key="v" :value="v">{{ v }}</SelectItem>
                </SelectContent>
              </Select>
            </label>

            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.video') }}
              <Select
                :model-value="selectModel(filters.videotype)"
                @update:model-value="(v) => onServer('videotype', String(v))"
              >
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="t('search.filters.all')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                  <SelectItem value="sdr">SDR</SelectItem>
                  <SelectItem value="hdr">HDR</SelectItem>
                </SelectContent>
              </Select>
            </label>

            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.year') }}
              <Select
                :model-value="selectModel(filters.year)"
                @update:model-value="(v) => onServer('year', String(v))"
              >
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="t('search.filters.all')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                  <SelectItem v-for="v in facets.year" :key="v" :value="v">{{ v }}</SelectItem>
                </SelectContent>
              </Select>
            </label>

            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.quality') }}
              <Select
                :model-value="selectModel(filters.quality)"
                @update:model-value="(v) => onServer('quality', String(v))"
              >
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="t('search.filters.all')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                  <SelectItem v-for="v in facets.quality" :key="v" :value="v">{{ v }}</SelectItem>
                </SelectContent>
              </Select>
            </label>

            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.season') }}
              <Select
                :model-value="selectModel(filters.season)"
                @update:model-value="(v) => onServer('season', String(v))"
              >
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="t('search.filters.all')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                  <SelectItem v-for="v in facets.season" :key="v" :value="v">{{ v }}</SelectItem>
                </SelectContent>
              </Select>
            </label>

            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.refine') }}
              <Input
                :model-value="filters.refine"
                :placeholder="t('search.filters.refinePlaceholder')"
                @update:model-value="(v) => emit('clientFilter', 'refine', String(v))"
              />
            </label>

            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.exclude') }}
              <Input
                :model-value="filters.exclude"
                :placeholder="t('search.filters.excludePlaceholder')"
                @update:model-value="(v) => emit('clientFilter', 'exclude', String(v))"
              />
            </label>

            <Button
              type="button"
              variant="ghost"
              size="sm"
              class="gap-1.5 justify-self-start"
              @click="emit('reset')"
            >
              <RotateCcw class="size-3.5" />
              {{ t('search.filters.reset') }}
            </Button>
        </div>
      </SheetContent>
    </Sheet>

    <!-- Desktop: inline Collapsible panel -->
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
          class="grid gap-2.5 rounded-xl border jr-glass-panel p-2.5 sm:grid-cols-2 lg:grid-cols-4"
        >
          <label class="space-y-1 text-xs text-muted-foreground">
            {{ t('search.filters.type') }}
            <Select
              :model-value="selectModel(filters.type)"
              @update:model-value="(v) => onServer('type', String(v))"
            >
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="t('search.filters.all')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                <SelectItem v-for="v in facets.type" :key="v" :value="v">{{ v }}</SelectItem>
              </SelectContent>
            </Select>
          </label>

          <label class="space-y-1 text-xs text-muted-foreground">
            {{ t('search.filters.tracker') }}
            <Select
              :model-value="selectModel(filters.tracker)"
              @update:model-value="(v) => onServer('tracker', String(v))"
            >
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="t('search.filters.all')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                <SelectItem v-for="v in facets.tracker" :key="v" :value="v">{{ v }}</SelectItem>
              </SelectContent>
            </Select>
          </label>

          <label class="space-y-1 text-xs text-muted-foreground">
            {{ t('search.filters.voice') }}
            <Select
              :model-value="selectModel(filters.voice)"
              @update:model-value="(v) => onServer('voice', String(v))"
            >
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="t('search.filters.all')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                <SelectItem v-for="v in facets.voice" :key="v" :value="v">{{ v }}</SelectItem>
              </SelectContent>
            </Select>
          </label>

          <label class="space-y-1 text-xs text-muted-foreground">
            {{ t('search.filters.video') }}
            <Select
              :model-value="selectModel(filters.videotype)"
              @update:model-value="(v) => onServer('videotype', String(v))"
            >
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="t('search.filters.all')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                <SelectItem value="sdr">SDR</SelectItem>
                <SelectItem value="hdr">HDR</SelectItem>
              </SelectContent>
            </Select>
          </label>

          <label class="space-y-1 text-xs text-muted-foreground">
            {{ t('search.filters.year') }}
            <Select
              :model-value="selectModel(filters.year)"
              @update:model-value="(v) => onServer('year', String(v))"
            >
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="t('search.filters.all')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                <SelectItem v-for="v in facets.year" :key="v" :value="v">{{ v }}</SelectItem>
              </SelectContent>
            </Select>
          </label>

          <label class="space-y-1 text-xs text-muted-foreground">
            {{ t('search.filters.quality') }}
            <Select
              :model-value="selectModel(filters.quality)"
              @update:model-value="(v) => onServer('quality', String(v))"
            >
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="t('search.filters.all')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                <SelectItem v-for="v in facets.quality" :key="v" :value="v">{{ v }}</SelectItem>
              </SelectContent>
            </Select>
          </label>

          <label class="space-y-1 text-xs text-muted-foreground">
            {{ t('search.filters.season') }}
            <Select
              :model-value="selectModel(filters.season)"
              @update:model-value="(v) => onServer('season', String(v))"
            >
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="t('search.filters.all')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                <SelectItem v-for="v in facets.season" :key="v" :value="v">{{ v }}</SelectItem>
              </SelectContent>
            </Select>
          </label>

          <div class="grid gap-2.5 sm:col-span-2 sm:grid-cols-2 lg:col-span-4 lg:grid-cols-2">
            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.refine') }}
              <Input
                :model-value="filters.refine"
                :placeholder="t('search.filters.refinePlaceholder')"
                @update:model-value="(v) => emit('clientFilter', 'refine', String(v))"
              />
            </label>
            <label class="space-y-1 text-xs text-muted-foreground">
              {{ t('search.filters.exclude') }}
              <Input
                :model-value="filters.exclude"
                :placeholder="t('search.filters.excludePlaceholder')"
                @update:model-value="(v) => emit('clientFilter', 'exclude', String(v))"
              />
            </label>
          </div>

          <div class="flex items-end sm:col-span-2 lg:col-span-4">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              class="gap-1.5"
              @click="emit('reset')"
            >
              <RotateCcw class="size-3.5" />
              {{ t('search.filters.reset') }}
            </Button>
          </div>
        </div>
      </CollapsibleContent>
    </Collapsible>
  </div>
</template>
