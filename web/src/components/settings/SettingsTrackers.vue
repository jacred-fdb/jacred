<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ChevronDown, Search } from '@lucide/vue'
import { Input } from '@/components/ui/input'
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible'
import SettingsField from '@/components/settings/SettingsField.vue'
import {
  getByPath,
  type ConfigFieldChange,
  type ConfigGroup,
} from '@/lib/config-schema'
import { partitionSettingsFields } from '@/lib/settings-layout'
import { cn } from '@/lib/utils'

const props = defineProps<{
  group: ConfigGroup
  data: Record<string, unknown>
}>()

const emit = defineEmits<{ change: [ConfigFieldChange] }>()
const { t } = useI18n()

const search = ref('')
const openId = ref<string | null>(null)

const trackers = computed(() => props.group.trackers || [])

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return trackers.value
  return trackers.value.filter((t) => {
    const host = String(
      getByPath(props.data, `${t.id}.host`) ?? '',
    ).toLowerCase()
    return t.title.toLowerCase().includes(q) || host.includes(q)
  })
})

const countLabel = computed(() => {
  const total = trackers.value.length
  const visible = filtered.value.length
  return visible === total
    ? t('settings.trackerCount', { count: total }, { plural: total })
    : t('settings.trackerFilteredCount', { visible, total })
})

const filteredWithParts = computed(() =>
  filtered.value.map((tracker) => ({
    tracker,
    parts: partitionSettingsFields(tracker.fields),
  })),
)

function setOpen(id: string, open: boolean) {
  openId.value = open ? id : null
}
</script>

<template>
  <div class="space-y-3">
    <div class="flex flex-wrap items-center gap-2">
      <div class="relative min-w-0 flex-1">
        <Search
          class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
        />
        <Input
          v-model="search"
          type="search"
          class="h-9 pl-9"
          :placeholder="t('settings.trackerSearch')"
          :aria-label="t('settings.trackerSearchAria')"
        />
      </div>
      <span class="text-xs text-muted-foreground">{{ countLabel }}</span>
    </div>

    <div class="space-y-2">
      <Collapsible
        v-for="{ tracker, parts } in filteredWithParts"
        :key="tracker.id"
        :open="openId === tracker.id"
        @update:open="(v) => setOpen(tracker.id, v)"
      >
        <div class="jr-glass-panel overflow-hidden rounded-xl border">
          <CollapsibleTrigger
            class="flex w-full items-center gap-2 px-3 py-2 text-left hover:bg-muted/30"
          >
            <ChevronDown
              :class="
                cn(
                  'size-4 shrink-0 transition-transform',
                  openId === tracker.id && 'rotate-180',
                )
              "
            />
            <span class="shrink-0 text-sm font-medium">{{ tracker.title }}</span>
            <span
              v-if="getByPath(data, `${tracker.id}.host`)"
              class="min-w-0 truncate text-xs text-muted-foreground"
            >
              {{ getByPath(data, `${tracker.id}.host`) }}
            </span>
            <span
              v-if="getByPath(data, `${tracker.id}.log`)"
              class="ml-auto shrink-0 rounded bg-muted px-1.5 py-0.5 text-xs uppercase tracking-wide text-muted-foreground"
            >
              log
            </span>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <div class="space-y-3 border-t border-border/60 p-3">
              <div
                v-if="parts.bools.length || parts.compact.length"
                class="jr-settings-grid"
              >
                <SettingsField
                  v-for="field in parts.bools"
                  :key="`${tracker.id}.${field.key}`"
                  :field="field"
                  :data="data"
                  :prefix="tracker.id"
                  @change="emit('change', $event)"
                />
                <SettingsField
                  v-for="field in parts.compact"
                  :key="`${tracker.id}.${field.key}`"
                  :field="field"
                  :data="data"
                  :prefix="tracker.id"
                  @change="emit('change', $event)"
                />
              </div>
              <div v-if="parts.wide.length" class="grid gap-3">
                <SettingsField
                  v-for="field in parts.wide"
                  :key="`${tracker.id}.${field.key}`"
                  :field="field"
                  :data="data"
                  :prefix="tracker.id"
                  @change="emit('change', $event)"
                />
              </div>
              <div
                v-if="parts.auth.length"
                class="jr-settings-grid jr-settings-grid--auth"
              >
                <SettingsField
                  v-for="field in parts.auth"
                  :key="`${tracker.id}.${field.key}`"
                  :field="field"
                  :data="data"
                  :prefix="tracker.id"
                  @change="emit('change', $event)"
                />
              </div>
            </div>
          </CollapsibleContent>
        </div>
      </Collapsible>
    </div>
  </div>
</template>
