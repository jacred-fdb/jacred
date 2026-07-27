<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

const open = defineModel<boolean>('open', { default: false })
const { t, locale } = useI18n()

const rows = computed(() => {
  void locale.value
  return [
    { keys: '/', desc: t('search.shortcutsDialog.focus') },
    { keys: 'Enter', desc: t('search.shortcutsDialog.search') },
    { keys: 'Esc', desc: t('search.shortcutsDialog.closeFilters') },
    { keys: '?', desc: t('search.shortcutsDialog.help') },
  ]
})
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{{ t('search.shortcutsDialog.title') }}</DialogTitle>
        <DialogDescription>
          {{ t('search.shortcutsDialog.description') }}
        </DialogDescription>
      </DialogHeader>
      <ul class="space-y-2 text-sm">
        <li
          v-for="row in rows"
          :key="row.keys"
          class="flex items-center justify-between gap-4 border-b border-border/60 py-2 last:border-0"
        >
          <span class="text-muted-foreground">{{ row.desc }}</span>
          <kbd
            class="rounded-md border border-border bg-muted px-2 py-0.5 font-mono text-xs"
          >
            {{ row.keys }}
          </kbd>
        </li>
      </ul>
    </DialogContent>
  </Dialog>
</template>
