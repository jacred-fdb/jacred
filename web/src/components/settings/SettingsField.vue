<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Eye, EyeOff } from '@lucide/vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  defaultJsonText,
  getByPath,
  normalizeRaw,
  parseJsonField,
  stringListToText,
  textToStringList,
  type ConfigField,
  type ConfigFieldChange,
} from '@/lib/config-schema'
import { isSettingsFullWidthField } from '@/lib/settings-layout'
import { cn } from '@/lib/utils'

const props = defineProps<{
  field: ConfigField
  data: Record<string, unknown>
  prefix?: string
}>()

const emit = defineEmits<{ change: [ConfigFieldChange] }>()
const { t } = useI18n()

const path = computed(() =>
  props.prefix ? `${props.prefix}.${props.field.key}` : props.field.key,
)
const fieldId = computed(
  () => `settings-${path.value.replace(/[^a-zA-Z0-9_-]/g, '-')}`,
)
const showPassword = ref(false)

const raw = computed(() =>
  normalizeRaw(getByPath(props.data, path.value), props.field),
)

const lastValidJson = ref('')

const textValue = computed({
  get() {
    if (props.field.type === 'stringList') return stringListToText(raw.value)
    if (props.field.type === 'json') {
      return defaultJsonText(props.field, raw.value)
    }
    if (props.field.type === 'int') {
      return raw.value == null || raw.value === '' ? '' : String(raw.value)
    }
    return raw.value == null ? '' : String(raw.value)
  },
  set(v: string) {
    apply(v)
  },
})

const boolValue = computed({
  get: () => !!raw.value,
  set(v: boolean) {
    emit('change', { path: path.value, value: v })
  },
})

const selectValue = computed({
  get: () => String(raw.value ?? ''),
  set(v: string) {
    apply(v)
  },
})

watch(
  () => raw.value,
  (v) => {
    if (props.field.type === 'json' && v != null) {
      try {
        lastValidJson.value = JSON.stringify(v)
      } catch {
        /* ignore */
      }
    }
  },
  { immediate: true },
)

function apply(v: string) {
  const f = props.field
  let value: unknown = v
  if (f.type === 'int') {
    const n = parseInt(v, 10)
    value = Number.isFinite(n) ? n : 0
  } else if (f.type === 'stringList') {
    value = textToStringList(v)
  } else if (f.type === 'json') {
    value = parseJsonField(v, f.key, lastValidJson.value)
    try {
      lastValidJson.value = JSON.stringify(value)
    } catch {
      /* ignore */
    }
  } else if (f.type === 'select' && f.key === 'tracksmod') {
    const n = parseInt(v, 10)
    value = Number.isFinite(n) ? n : 0
  }
  emit('change', { path: path.value, value })
}

const fullWidth = computed(() => isSettingsFullWidthField(props.field))
const isBool = computed(() => props.field.type === 'bool')
</script>

<template>
  <div
    :class="
      cn(
        'jr-settings-field',
        isBool && 'jr-settings-field--bool',
        fullWidth && 'col-span-full',
      )
    "
  >
    <template v-if="isBool">
      <div class="jr-settings-field__control flex w-full items-center justify-between gap-3">
        <div class="min-w-0">
          <label
            :for="fieldId"
            class="jr-settings-field__label block text-sm leading-5 font-medium"
          >
            {{ field.label }}
          </label>
          <p
            v-if="field.description"
            class="jr-settings-field__desc text-xs leading-4 text-muted-foreground"
          >
            {{ field.description }}
          </p>
        </div>
        <Switch :id="fieldId" v-model="boolValue" class="shrink-0" />
      </div>
    </template>

    <template v-else>
      <label
        :for="fieldId"
        class="jr-settings-field__label block text-sm leading-5 font-medium"
      >
        {{ field.label }}
      </label>
      <p
        v-if="field.description"
        class="jr-settings-field__desc text-xs leading-4 text-muted-foreground"
      >
        {{ field.description }}
      </p>
      <!-- Reserve desc space when missing so neighbors share a control baseline -->
      <p
        v-else
        class="jr-settings-field__desc text-xs leading-4 opacity-0"
        aria-hidden="true"
      >
        &nbsp;
      </p>

      <div class="jr-settings-field__control">
        <textarea
          v-if="field.type === 'stringList' || field.type === 'json'"
          :id="fieldId"
          :value="textValue"
          :rows="field.type === 'json' ? 6 : 4"
          spellcheck="false"
          class="border-input dark:bg-input/30 placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 flex min-h-[80px] w-full rounded-lg border bg-transparent px-2.5 py-2 font-mono text-sm outline-none focus-visible:ring-3"
          @input="textValue = ($event.target as HTMLTextAreaElement).value"
        />

        <Select
          v-else-if="field.type === 'select'"
          :model-value="selectValue"
          @update:model-value="(v) => (selectValue = String(v))"
        >
          <SelectTrigger :id="fieldId" class="h-9 w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem
              v-for="opt in field.enumValues || []"
              :key="opt"
              :value="opt"
            >
              {{ opt }}
            </SelectItem>
          </SelectContent>
        </Select>

        <Input
          v-else-if="field.type === 'int'"
          :id="fieldId"
          type="number"
          class="h-9"
          :model-value="textValue"
          :min="field.min ?? undefined"
          :max="field.max ?? undefined"
          @update:model-value="(v) => (textValue = String(v))"
        />

        <div v-else-if="field.type === 'password'" class="relative">
          <Input
            :id="fieldId"
            :type="showPassword ? 'text' : 'password'"
            class="h-9 pr-10 font-mono"
            :model-value="textValue"
            autocomplete="new-password"
            @update:model-value="(v) => (textValue = String(v))"
          />
          <Button
            type="button"
            variant="ghost"
            size="icon"
            class="absolute top-1/2 right-0.5 size-8 -translate-y-1/2"
            :aria-label="
              showPassword
                ? t('settings.hidePassword')
                : t('settings.showPassword')
            "
            @click="showPassword = !showPassword"
          >
            <EyeOff v-if="showPassword" class="size-4" />
            <Eye v-else class="size-4" />
          </Button>
        </div>

        <Input
          v-else
          :id="fieldId"
          type="text"
          class="h-9"
          :model-value="textValue"
          autocomplete="off"
          @update:model-value="(v) => (textValue = String(v))"
        />
      </div>
    </template>
  </div>
</template>
