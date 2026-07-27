<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ChevronDown } from '@lucide/vue'
import {
  PopoverContent,
  PopoverPortal,
  PopoverRoot,
  PopoverTrigger,
} from 'reka-ui'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

export type FilterMultiOption = string | { value: string; label: string }

const props = withDefaults(
  defineProps<{
    label: string
    options: FilterMultiOption[]
    selected: string[]
    emptyText?: string
    searchable?: boolean
    /** Raise z-index inside mobile sheet. */
    mobile?: boolean
  }>(),
  {
    emptyText: '',
    searchable: false,
    mobile: false,
  },
)

const emit = defineEmits<{
  toggle: [value: string]
  clear: []
}>()

const { t } = useI18n()
const open = ref(false)
const query = ref('')

watch(open, (v) => {
  if (!v) query.value = ''
})

function asOption(o: FilterMultiOption) {
  return typeof o === 'string' ? { value: o, label: o } : o
}

const items = computed(() => props.options.map(asOption))

const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return items.value
  return items.value.filter(
    (o) =>
      o.label.toLowerCase().includes(q) || o.value.toLowerCase().includes(q),
  )
})

function isOn(value: string) {
  return props.selected.some((s) => s.toLowerCase() === value.toLowerCase())
}

const summary = computed(() => {
  if (!props.selected.length) return t('search.filters.all')
  if (props.selected.length === 1) {
    const one = props.selected[0]
    const hit = items.value.find(
      (o) => o.value.toLowerCase() === one.toLowerCase(),
    )
    return hit?.label ?? one
  }
  return t('search.filters.selectedCount', { count: props.selected.length })
})

const triggerClass =
  'flex h-9 w-full items-center justify-between gap-2 rounded-[var(--radius-sm)] border border-border bg-background px-3 text-left text-sm shadow-none outline-none transition-[background-color,border-color,transform] duration-100 hover:bg-muted/50 focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/40 active:scale-[0.99] disabled:cursor-not-allowed disabled:opacity-50 dark:bg-background dark:hover:bg-muted/50 motion-reduce:active:scale-100'
</script>

<template>
  <div class="jr-filter-field">
    <div class="jr-filter-field__row">
      <span class="jr-filter-field__label">{{ label }}</span>
      <span
        v-if="selected.length"
        class="jr-filter-field__count"
      >
        {{ selected.length }}
      </span>
    </div>

    <div class="jr-filter-field__control">
      <p v-if="!items.length" class="jr-filter-empty">
        {{ emptyText || t('search.filters.facetEmpty') }}
      </p>

      <PopoverRoot v-else v-model:open="open">
        <PopoverTrigger
          :class="cn(triggerClass, open && 'border-ring ring-2 ring-ring/40')"
          :aria-label="label"
        >
          <span
            class="min-w-0 flex-1 truncate"
            :class="selected.length ? 'text-foreground' : 'text-muted-foreground'"
          >
            {{ summary }}
          </span>
          <ChevronDown
            class="size-4 shrink-0 text-muted-foreground transition-transform duration-150"
            :class="open && 'rotate-180'"
            aria-hidden="true"
          />
        </PopoverTrigger>

        <PopoverPortal>
          <PopoverContent
            align="start"
            :side-offset="6"
            :collision-padding="12"
            :class="
              cn(
                'jr-filter-multiselect z-50 w-[var(--reka-popover-trigger-width)] min-w-[16rem] overflow-hidden rounded-xl border border-border bg-popover p-0 text-popover-foreground shadow-lg outline-none',
                'data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0 data-closed:zoom-out-95 data-open:zoom-in-95',
                mobile && 'z-[60] ![backdrop-filter:none] ![-webkit-backdrop-filter:none]',
              )
            "
            @open-auto-focus="(e: Event) => searchable && e.preventDefault()"
          >
            <div
              v-if="searchable && items.length > 8"
              class="border-b border-border p-2"
            >
              <Input
                v-model="query"
                type="search"
                :placeholder="t('search.filters.searchOptions')"
                class="h-8 rounded-lg border-border bg-background text-sm shadow-none"
                @keydown.stop
              />
            </div>

            <div
              class="jr-filter-multiselect__list max-h-56 overflow-y-auto overscroll-contain p-1.5"
              role="group"
              :aria-label="label"
            >
              <p
                v-if="!filtered.length"
                class="px-2 py-3 text-center text-xs text-muted-foreground"
              >
                {{ t('search.filters.noOptions') }}
              </p>
              <label
                v-for="opt in filtered"
                :key="opt.value"
                class="jr-filter-multiselect__option"
              >
                <input
                  type="checkbox"
                  class="size-4 shrink-0 accent-[var(--primary)]"
                  :checked="isOn(opt.value)"
                  @change="emit('toggle', opt.value)"
                />
                <span class="min-w-0 flex-1 truncate">{{ opt.label }}</span>
              </label>
            </div>

            <div
              v-if="selected.length"
              class="flex items-center justify-end border-t border-border px-2 py-1.5"
            >
              <Button
                type="button"
                variant="ghost"
                size="sm"
                class="h-7 px-2 text-xs text-muted-foreground"
                @click="emit('clear')"
              >
                {{ t('search.filters.clearSelection') }}
              </Button>
            </div>
          </PopoverContent>
        </PopoverPortal>
      </PopoverRoot>
    </div>
  </div>
</template>
