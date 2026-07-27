<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  getByPath,
  type ConfigField,
  type ConfigFieldChange,
} from '@/lib/config-schema'

const props = defineProps<{
  field: ConfigField
  data: Record<string, unknown>
}>()

const emit = defineEmits<{ change: [ConfigFieldChange] }>()
const { t, locale } = useI18n()

const selected = computed(() => {
  const raw = getByPath(props.data, props.field.key)
  return new Set(Array.isArray(raw) ? (raw as string[]) : [])
})

const count = computed(() => selected.value.size)

function toggle(slug: string, on: boolean) {
  const next = new Set(selected.value)
  if (on) next.add(slug)
  else next.delete(slug)
  emit('change', {
    path: props.field.key,
    value: Array.from(next).sort((a, b) => a.localeCompare(b, locale.value)),
  })
}
</script>

<template>
  <div class="space-y-2">
    <div class="flex items-baseline justify-between gap-2">
      <label class="text-sm font-medium">{{ field.label }}</label>
      <span class="text-xs text-muted-foreground">
        {{ t('settings.selectedCount', { count }) }}
      </span>
    </div>
    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
    <div class="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
      <label
        v-for="slug in field.enumValues || []"
        :key="slug"
        class="flex min-h-9 cursor-pointer items-center gap-2 rounded-lg border border-border/70 bg-background px-2.5 py-2 text-sm hover:bg-muted/40"
      >
        <input
          type="checkbox"
          class="size-4 shrink-0 accent-[var(--primary)]"
          :checked="selected.has(slug)"
          @change="
            toggle(slug, ($event.target as HTMLInputElement).checked)
          "
        />
        <span class="truncate font-mono text-xs">{{ slug }}</span>
      </label>
    </div>
  </div>
</template>
