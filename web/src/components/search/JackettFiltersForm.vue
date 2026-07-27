<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import FilterMultiSelect from '@/components/search/FilterMultiSelect.vue'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  V2_CATEGORY_OPTIONS,
  V2_IS_SERIAL_OPTIONS,
  V2_QUALITY_OPTIONS,
  type V2SearchFilters,
} from '@/lib/jackett'

const props = defineProps<{
  filters: V2SearchFilters
  facets: {
    tracker: string[]
    voice: string[]
    season: string[]
    lang: string[]
  }
  /** Use solid popover styles in mobile sheet. */
  mobileSelect?: boolean
}>()

const emit = defineEmits<{
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
}>()

const { t } = useI18n()

const fieldControl =
  'h-9 w-full min-w-0 rounded-[var(--radius-sm)] border border-border bg-background px-3 py-0 text-sm shadow-none ring-0 focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/40 dark:bg-background dark:hover:bg-muted/50 data-[size=default]:!h-9'
const mobileSelectContentClass =
  'z-[60] w-[var(--reka-select-trigger-width)] !bg-popover shadow-lg ![backdrop-filter:none] ![-webkit-backdrop-filter:none]'

const categoryOptions = computed(() =>
  V2_CATEGORY_OPTIONS.map((o) => ({
    value: o.value,
    label: t(o.labelKey),
  })),
)

const qualityOptions = computed(() =>
  V2_QUALITY_OPTIONS.map((o) => ({
    value: o.value,
    label: t(o.labelKey),
  })),
)

function selectModel(value: string) {
  return value || '__all__'
}

function onV2(
  key: 'title' | 'titleOriginal' | 'year' | 'isSerial' | 'videotype',
  value: string | undefined,
) {
  emit('v2Filter', key, value === '__all__' ? '' : (value ?? ''))
}

function clearList(
  key: 'trackers' | 'qualities' | 'voices' | 'seasons' | 'langs',
) {
  for (const v of [...props.filters[key]]) {
    emit('toggleList', key, v)
  }
}

function clearCategories() {
  for (const v of [...props.filters.categories]) {
    emit('toggleCategory', v)
  }
}
</script>

<template>
  <div class="jr-jackett-filters">
    <section class="jr-filter-section" aria-labelledby="jr-filter-search-heading">
      <header class="jr-filter-section__head">
        <h3 id="jr-filter-search-heading" class="jr-filter-section__title">
          {{ t('search.filters.sectionSearch') }}
        </h3>
        <p class="jr-filter-section__hint">
          {{ t('search.filters.sectionSearchHint') }}
        </p>
      </header>

      <div class="jr-filters-facets jr-filters-facets--search">
        <div class="jr-filter-field">
          <div class="jr-filter-field__row">
            <span class="jr-filter-field__label">{{ t('search.filters.title') }}</span>
          </div>
          <div class="jr-filter-field__control">
            <Input
              :model-value="filters.title"
              :class="fieldControl"
              :placeholder="t('search.filters.titlePlaceholder')"
              @update:model-value="(v) => onV2('title', String(v))"
            />
          </div>
        </div>
        <div class="jr-filter-field">
          <div class="jr-filter-field__row">
            <span class="jr-filter-field__label">{{ t('search.filters.titleOriginal') }}</span>
          </div>
          <div class="jr-filter-field__control">
            <Input
              :model-value="filters.titleOriginal"
              :class="fieldControl"
              :placeholder="t('search.filters.titleOriginalPlaceholder')"
              @update:model-value="(v) => onV2('titleOriginal', String(v))"
            />
          </div>
        </div>
        <div class="jr-filter-field">
          <div class="jr-filter-field__row">
            <span class="jr-filter-field__label">{{ t('search.filters.year') }}</span>
          </div>
          <div class="jr-filter-field__control">
            <Input
              :model-value="filters.year"
              inputmode="numeric"
              :class="fieldControl"
              :placeholder="t('search.filters.yearPlaceholder')"
              @update:model-value="(v) => onV2('year', String(v))"
            />
          </div>
        </div>
        <div class="jr-filter-field">
          <div class="jr-filter-field__row">
            <span class="jr-filter-field__label">{{ t('search.filters.isSerial') }}</span>
          </div>
          <div class="jr-filter-field__control">
            <Select
              :model-value="selectModel(filters.isSerial)"
              @update:model-value="(v) => onV2('isSerial', String(v))"
            >
              <SelectTrigger :class="fieldControl">
                <SelectValue :placeholder="t('search.filters.all')" />
              </SelectTrigger>
              <SelectContent
                :position="mobileSelect ? 'popper' : undefined"
                :class="mobileSelect ? mobileSelectContentClass : undefined"
              >
                <SelectItem
                  v-for="opt in V2_IS_SERIAL_OPTIONS"
                  :key="opt.value || '__all__'"
                  :value="opt.value || '__all__'"
                >
                  {{ t(opt.labelKey) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        <FilterMultiSelect
          :label="t('search.filters.categories')"
          :options="categoryOptions"
          :selected="filters.categories"
          :mobile="mobileSelect"
          @toggle="(v) => emit('toggleCategory', v)"
          @clear="clearCategories"
        />
      </div>
    </section>

    <section class="jr-filter-section" aria-labelledby="jr-filter-results-heading">
      <header class="jr-filter-section__head">
        <h3 id="jr-filter-results-heading" class="jr-filter-section__title">
          {{ t('search.filters.sectionResults') }}
        </h3>
        <p class="jr-filter-section__hint">
          {{ t('search.filters.sectionResultsHint') }}
        </p>
      </header>

      <div class="jr-filter-section__stack">
        <div class="jr-filters-facets jr-filters-facets--narrow">
          <FilterMultiSelect
            :label="t('search.filters.quality')"
            :options="qualityOptions"
            :selected="filters.qualities"
            :mobile="mobileSelect"
            @toggle="(v) => emit('toggleList', 'qualities', v)"
            @clear="clearList('qualities')"
          />

          <div class="jr-filter-field">
            <div class="jr-filter-field__row">
              <span class="jr-filter-field__label">{{ t('search.filters.video') }}</span>
            </div>
            <div class="jr-filter-field__control">
              <Select
                :model-value="selectModel(filters.videotype)"
                @update:model-value="(v) => onV2('videotype', String(v))"
              >
                <SelectTrigger :class="fieldControl">
                  <SelectValue :placeholder="t('search.filters.all')" />
                </SelectTrigger>
                <SelectContent
                  :position="mobileSelect ? 'popper' : undefined"
                  :class="mobileSelect ? mobileSelectContentClass : undefined"
                >
                  <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
                  <SelectItem value="sdr">SDR</SelectItem>
                  <SelectItem value="hdr">HDR</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <FilterMultiSelect
            :label="t('search.filters.tracker')"
            :options="facets.tracker"
            :selected="filters.trackers"
            :empty-text="t('search.filters.trackerEmpty')"
            searchable
            :mobile="mobileSelect"
            @toggle="(v) => emit('toggleList', 'trackers', v)"
            @clear="clearList('trackers')"
          />

          <FilterMultiSelect
            :label="t('search.filters.voice')"
            :options="facets.voice"
            :selected="filters.voices"
            :empty-text="t('search.filters.facetEmpty')"
            searchable
            :mobile="mobileSelect"
            @toggle="(v) => emit('toggleList', 'voices', v)"
            @clear="clearList('voices')"
          />

          <FilterMultiSelect
            :label="t('search.filters.season')"
            :options="facets.season"
            :selected="filters.seasons"
            :empty-text="t('search.filters.facetEmpty')"
            :mobile="mobileSelect"
            @toggle="(v) => emit('toggleList', 'seasons', v)"
            @clear="clearList('seasons')"
          />

          <FilterMultiSelect
            :label="t('search.filters.lang')"
            :options="facets.lang"
            :selected="filters.langs"
            :empty-text="t('search.filters.facetEmpty')"
            :mobile="mobileSelect"
            @toggle="(v) => emit('toggleList', 'langs', v)"
            @clear="clearList('langs')"
          />
        </div>

        <div class="jr-filters-facets jr-filters-facets--title">
          <div class="jr-filter-field">
            <div class="jr-filter-field__row">
              <span class="jr-filter-field__label">{{ t('search.filters.refine') }}</span>
            </div>
            <div class="jr-filter-field__control">
              <Input
                :model-value="filters.refine"
                :class="fieldControl"
                :placeholder="t('search.filters.refinePlaceholder')"
                @update:model-value="(v) => emit('clientFilter', 'refine', String(v))"
              />
            </div>
          </div>
          <div class="jr-filter-field">
            <div class="jr-filter-field__row">
              <span class="jr-filter-field__label">{{ t('search.filters.exclude') }}</span>
            </div>
            <div class="jr-filter-field__control">
              <Input
                :model-value="filters.exclude"
                :class="fieldControl"
                :placeholder="t('search.filters.excludePlaceholder')"
                @update:model-value="(v) => emit('clientFilter', 'exclude', String(v))"
              />
            </div>
          </div>
        </div>
      </div>
    </section>
  </div>
</template>
