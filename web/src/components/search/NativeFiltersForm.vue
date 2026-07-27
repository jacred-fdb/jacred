<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { SearchFilters } from '@/lib/torrents'

defineProps<{
  filters: SearchFilters
  facets: {
    type: string[]
    tracker: string[]
    voice: string[]
    year: string[]
    quality: string[]
    season: string[]
  }
  /** Use solid popover styles in mobile sheet. */
  mobileSelect?: boolean
}>()

const emit = defineEmits<{
  serverFilter: [key: keyof SearchFilters, value: string]
  clientFilter: [key: 'refine' | 'exclude', value: string]
}>()

const { t } = useI18n()

const fieldLabel =
  'block space-y-1.5 text-xs font-medium text-[color:var(--jr-label)]'
const fieldControl =
  'h-9 w-full rounded-[var(--radius-sm)] border border-border bg-background text-sm shadow-none ring-0 focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/40 dark:bg-background dark:hover:bg-muted/50'
const mobileSelectContentClass =
  'z-[60] w-[var(--reka-select-trigger-width)] !bg-popover shadow-lg ![backdrop-filter:none] ![-webkit-backdrop-filter:none]'

function selectModel(value: string) {
  return value || '__all__'
}

function onServer(key: keyof SearchFilters, value: string | undefined) {
  emit('serverFilter', key, value === '__all__' ? '' : (value ?? ''))
}
</script>

<template>
  <div class="jr-native-filters">
    <section class="jr-filter-section" aria-labelledby="jr-native-results-heading">
      <header class="jr-filter-section__head">
        <h3 id="jr-native-results-heading" class="jr-filter-section__title">
          {{ t('search.filters.sectionResults') }}
        </h3>
        <p class="jr-filter-section__hint">
          {{ t('search.filters.sectionNativeHint') }}
        </p>
      </header>

      <div class="jr-filters-facets">
        <label :class="fieldLabel">
          {{ t('search.filters.type') }}
          <Select
            :model-value="selectModel(filters.type)"
            @update:model-value="(v) => onServer('type', String(v))"
          >
            <SelectTrigger :class="fieldControl">
              <SelectValue :placeholder="t('search.filters.all')" />
            </SelectTrigger>
            <SelectContent
              :position="mobileSelect ? 'popper' : undefined"
              :class="mobileSelect ? mobileSelectContentClass : undefined"
            >
              <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
              <SelectItem
                v-for="v in facets.type"
                :key="v"
                :value="v"
              >
                {{ v }}
              </SelectItem>
            </SelectContent>
          </Select>
        </label>

        <label :class="fieldLabel">
          {{ t('search.filters.tracker') }}
          <Select
            :model-value="selectModel(filters.tracker)"
            @update:model-value="(v) => onServer('tracker', String(v))"
          >
            <SelectTrigger :class="fieldControl">
              <SelectValue :placeholder="t('search.filters.all')" />
            </SelectTrigger>
            <SelectContent
              :position="mobileSelect ? 'popper' : undefined"
              :class="mobileSelect ? mobileSelectContentClass : undefined"
            >
              <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
              <SelectItem
                v-for="v in facets.tracker"
                :key="v"
                :value="v"
              >
                {{ v }}
              </SelectItem>
            </SelectContent>
          </Select>
        </label>

        <label :class="fieldLabel">
          {{ t('search.filters.voice') }}
          <Select
            :model-value="selectModel(filters.voice)"
            @update:model-value="(v) => onServer('voice', String(v))"
          >
            <SelectTrigger :class="fieldControl">
              <SelectValue :placeholder="t('search.filters.all')" />
            </SelectTrigger>
            <SelectContent
              :position="mobileSelect ? 'popper' : undefined"
              :class="mobileSelect ? mobileSelectContentClass : undefined"
            >
              <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
              <SelectItem
                v-for="v in facets.voice"
                :key="v"
                :value="v"
              >
                {{ v }}
              </SelectItem>
            </SelectContent>
          </Select>
        </label>

        <label :class="fieldLabel">
          {{ t('search.filters.video') }}
          <Select
            :model-value="selectModel(filters.videotype)"
            @update:model-value="(v) => onServer('videotype', String(v))"
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
        </label>

        <label :class="fieldLabel">
          {{ t('search.filters.year') }}
          <Select
            :model-value="selectModel(filters.year)"
            @update:model-value="(v) => onServer('year', String(v))"
          >
            <SelectTrigger :class="fieldControl">
              <SelectValue :placeholder="t('search.filters.all')" />
            </SelectTrigger>
            <SelectContent
              :position="mobileSelect ? 'popper' : undefined"
              :class="mobileSelect ? mobileSelectContentClass : undefined"
            >
              <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
              <SelectItem
                v-for="v in facets.year"
                :key="v"
                :value="v"
              >
                {{ v }}
              </SelectItem>
            </SelectContent>
          </Select>
        </label>

        <label :class="fieldLabel">
          {{ t('search.filters.quality') }}
          <Select
            :model-value="selectModel(filters.quality)"
            @update:model-value="(v) => onServer('quality', String(v))"
          >
            <SelectTrigger :class="fieldControl">
              <SelectValue :placeholder="t('search.filters.all')" />
            </SelectTrigger>
            <SelectContent
              :position="mobileSelect ? 'popper' : undefined"
              :class="mobileSelect ? mobileSelectContentClass : undefined"
            >
              <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
              <SelectItem
                v-for="v in facets.quality"
                :key="v"
                :value="v"
              >
                {{ v }}
              </SelectItem>
            </SelectContent>
          </Select>
        </label>

        <label :class="fieldLabel">
          {{ t('search.filters.season') }}
          <Select
            :model-value="selectModel(filters.season)"
            @update:model-value="(v) => onServer('season', String(v))"
          >
            <SelectTrigger :class="fieldControl">
              <SelectValue :placeholder="t('search.filters.all')" />
            </SelectTrigger>
            <SelectContent
              :position="mobileSelect ? 'popper' : undefined"
              :class="mobileSelect ? mobileSelectContentClass : undefined"
            >
              <SelectItem value="__all__">{{ t('search.filters.all') }}</SelectItem>
              <SelectItem
                v-for="v in facets.season"
                :key="v"
                :value="v"
              >
                {{ v }}
              </SelectItem>
            </SelectContent>
          </Select>
        </label>
      </div>
    </section>

    <section class="jr-filter-section" aria-labelledby="jr-native-title-heading">
      <header class="jr-filter-section__head">
        <h3 id="jr-native-title-heading" class="jr-filter-section__title">
          {{ t('search.filters.sectionTitle') }}
        </h3>
      </header>
      <div class="jr-filter-grid">
        <label :class="fieldLabel">
          {{ t('search.filters.refine') }}
          <Input
            :model-value="filters.refine"
            :class="fieldControl"
            :placeholder="t('search.filters.refinePlaceholder')"
            @update:model-value="(v) => emit('clientFilter', 'refine', String(v))"
          />
        </label>
        <label :class="fieldLabel">
          {{ t('search.filters.exclude') }}
          <Input
            :model-value="filters.exclude"
            :class="fieldControl"
            :placeholder="t('search.filters.excludePlaceholder')"
            @update:model-value="(v) => emit('clientFilter', 'exclude', String(v))"
          />
        </label>
      </div>
    </section>
  </div>
</template>
