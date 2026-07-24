<script setup lang="ts">
import { computed } from 'vue'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import SettingsCheckboxList from '@/components/settings/SettingsCheckboxList.vue'
import SettingsField from '@/components/settings/SettingsField.vue'
import SettingsTrackers from '@/components/settings/SettingsTrackers.vue'
import {
  resolveActiveTab,
  tabIdForGroup,
  type ConfigField,
  type ConfigFieldChange,
  type ConfigSchema,
} from '@/lib/config-schema'
import { settingsGroupIcon } from '@/lib/settings-icons'
import { partitionSettingsFields } from '@/lib/settings-layout'

const props = defineProps<{
  schema: ConfigSchema
  data: Record<string, unknown>
  activeTab: string | null
}>()

const emit = defineEmits<{
  change: [ConfigFieldChange]
  'update:activeTab': [string]
}>()

const groups = computed(() => props.schema.groups || [])

const currentTab = computed(() => {
  return (
    resolveActiveTab(props.schema, props.activeTab) ||
    (groups.value[0] ? tabIdForGroup(groups.value[0]) : null)
  )
})

const activeGroup = computed(() =>
  groups.value.find((g) => tabIdForGroup(g) === currentTab.value) || null,
)

function isCheckboxList(field: ConfigField) {
  return (
    field.type === 'stringList' &&
    !!field.enumValues?.length &&
    (field.key === 'synctrackers' || field.key === 'disable_trackers')
  )
}

const groupLayout = computed(() => {
  const fields = activeGroup.value?.fields || []
  const checkboxLists = fields.filter(isCheckboxList)
  const rest = fields.filter((f) => !isCheckboxList(f))
  return {
    checkboxLists,
    ...partitionSettingsFields(rest),
  }
})
</script>

<template>
  <div class="space-y-4">
    <ToggleGroup
      v-if="groups.length"
      type="single"
      :model-value="currentTab ?? undefined"
      variant="outline"
      size="sm"
      class="flex w-full flex-wrap justify-start gap-1.5"
      @update:model-value="(v) => v && emit('update:activeTab', String(v))"
    >
      <ToggleGroupItem
        v-for="group in groups"
        :key="group.id"
        :value="tabIdForGroup(group)"
        class="h-8 gap-1.5 px-2.5"
      >
        <component
          :is="settingsGroupIcon(group.id)"
          class="size-3.5 shrink-0 opacity-80"
          aria-hidden="true"
        />
        {{ group.title }}
      </ToggleGroupItem>
    </ToggleGroup>

    <div v-if="activeGroup" class="space-y-3">
      <p
        v-if="activeGroup.description"
        class="text-sm text-muted-foreground"
      >
        {{ activeGroup.description }}
      </p>

      <SettingsTrackers
        v-if="activeGroup.id === 'trackers'"
        :group="activeGroup"
        :data="data"
        @change="emit('change', $event)"
      />

      <div
        v-else
        class="space-y-3 rounded-xl border jr-glass-panel p-3"
      >
        <div
          v-if="groupLayout.bools.length"
          class="grid items-stretch gap-2 sm:grid-cols-2 lg:grid-cols-3"
        >
          <SettingsField
            v-for="field in groupLayout.bools"
            :key="field.key"
            :field="field"
            :data="data"
            @change="emit('change', $event)"
          />
        </div>
        <div
          v-if="groupLayout.compact.length"
          class="grid items-start gap-3 sm:grid-cols-2 lg:grid-cols-3"
        >
          <SettingsField
            v-for="field in groupLayout.compact"
            :key="field.key"
            :field="field"
            :data="data"
            @change="emit('change', $event)"
          />
        </div>
        <div v-if="groupLayout.wide.length" class="grid gap-3">
          <SettingsField
            v-for="field in groupLayout.wide"
            :key="field.key"
            :field="field"
            :data="data"
            @change="emit('change', $event)"
          />
        </div>
        <div
          v-if="groupLayout.auth.length"
          class="grid items-start gap-3 sm:grid-cols-2"
        >
          <SettingsField
            v-for="field in groupLayout.auth"
            :key="field.key"
            :field="field"
            :data="data"
            @change="emit('change', $event)"
          />
        </div>
        <div
          v-for="field in groupLayout.checkboxLists"
          :key="field.key"
          class="col-span-full"
        >
          <SettingsCheckboxList
            :field="field"
            :data="data"
            @change="emit('change', $event)"
          />
        </div>
      </div>
    </div>
  </div>
</template>
