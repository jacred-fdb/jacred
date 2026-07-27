<script setup lang="ts">
import { computed } from 'vue'
import { Codemirror } from 'vue-codemirror'
import { json } from '@codemirror/lang-json'
import { yaml } from '@codemirror/lang-yaml'
import { oneDark } from '@codemirror/theme-one-dark'
import { EditorView } from '@codemirror/view'
import { useTheme } from '@/composables/useTheme'
import type { ConfigFormat } from '@/lib/config-schema'

const content = defineModel<string>('content', { default: '' })

const props = defineProps<{
  format: ConfigFormat
  disabled?: boolean
}>()

const emit = defineEmits<{ change: [] }>()

const { isDark } = useTheme()

const extensions = computed(() => {
  const lang = props.format === 'json' ? json() : yaml()
  const list = [
    lang,
    EditorView.lineWrapping,
    EditorView.theme({
      '&': { height: 'min(70vh, 640px)', fontSize: '13px' },
      '.cm-scroller': { overflow: 'auto' },
    }),
  ]
  if (isDark.value) list.push(oneDark)
  return list
})
</script>

<template>
  <div class="jr-elevated overflow-hidden rounded-xl border">
    <Codemirror
      v-model="content"
      :extensions="extensions"
      :disabled="disabled"
      :autofocus="false"
      :indent-with-tab="true"
      :tab-size="2"
      class="text-sm"
      @change="emit('change')"
    />
  </div>
</template>
