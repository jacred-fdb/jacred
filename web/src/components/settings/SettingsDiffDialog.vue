<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { ScrollArea } from '@/components/ui/scroll-area'
import {
  formatConfigValue,
  type ConfigDiffResponse,
} from '@/lib/config-schema'
import { cn } from '@/lib/utils'

const open = defineModel<boolean>('open', { default: false })

const props = defineProps<{
  diff: ConfigDiffResponse | null
}>()

const emit = defineEmits<{ confirm: [] }>()
const { t } = useI18n()

const canSave = computed(() => props.diff?.validation?.ok !== false)

const diffs = computed(() => props.diff?.diffs ?? [])
const changeCount = computed(
  () => props.diff?.changeCount ?? diffs.value.length,
)
const validation = computed(() => props.diff?.validation)
const changeLabel = computed(() =>
  t(
    'settings.diff.changeCount',
    { count: changeCount.value },
    { plural: changeCount.value },
  ),
)
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="flex max-h-[85vh] flex-col gap-0 sm:max-w-3xl">
      <DialogHeader>
        <DialogTitle>{{ t('settings.diff.title') }}</DialogTitle>
        <DialogDescription>
          {{ t('settings.diff.description', { count: changeLabel }) }}
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-3 py-3">
        <div
          v-if="validation?.errors?.length"
          class="rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
        >
          <strong>{{ t('settings.errorsLabel') }}</strong>
          <ul class="mt-1 list-disc pl-4">
            <li v-for="(e, i) in validation.errors" :key="i">{{ e }}</li>
          </ul>
        </div>
        <div
          v-else-if="validation?.error"
          class="rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
        >
          {{ validation.error }}
        </div>
        <div
          v-else-if="validation?.warnings?.length"
          class="jr-tone-warning rounded-lg border px-3 py-2 text-sm"
        >
          <strong>{{ t('settings.warningsLabel') }}</strong>
          <ul class="mt-1 list-disc pl-4">
            <li v-for="(w, i) in validation.warnings" :key="i">{{ w }}</li>
          </ul>
        </div>
        <div
          v-else-if="validation?.ok"
          class="jr-tone-success rounded-lg border px-3 py-2 text-sm"
        >
          {{ t('settings.configValid') }}
        </div>

        <ScrollArea class="h-[min(40vh,360px)] rounded-lg border border-border">
          <table class="w-full text-sm">
            <thead class="sticky top-0 z-10 bg-muted">
              <tr class="text-left">
                <th class="px-3 py-2 font-medium">{{ t('settings.diff.path') }}</th>
                <th class="px-3 py-2 font-medium">{{ t('settings.diff.before') }}</th>
                <th class="px-3 py-2 font-medium">{{ t('settings.diff.after') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, i) in diffs"
                :key="i"
                :class="
                  cn(
                    'border-t border-border/60',
                    row.change === 'added' && 'jr-tone-success-row',
                    row.change === 'removed' && 'bg-destructive/5',
                    row.change === 'changed' && 'jr-tone-warning-row',
                  )
                "
              >
                <td class="px-3 py-2 align-top font-mono text-xs">
                  {{ row.path }}
                </td>
                <td class="px-3 py-2 align-top break-all font-mono text-xs text-muted-foreground">
                  {{ formatConfigValue(row.oldValue, row.sensitive) }}
                </td>
                <td class="px-3 py-2 align-top break-all font-mono text-xs">
                  {{ formatConfigValue(row.newValue, row.sensitive) }}
                </td>
              </tr>
              <tr v-if="!diffs.length">
                <td
                  colspan="3"
                  class="px-3 py-6 text-center text-muted-foreground"
                >
                  {{ t('settings.diff.noChanges') }}
                </td>
              </tr>
            </tbody>
          </table>
        </ScrollArea>
      </div>

      <DialogFooter>
        <Button type="button" variant="outline" @click="open = false">
          {{ t('app.cancel') }}
        </Button>
        <Button
          type="button"
          :disabled="!canSave"
          @click="emit('confirm')"
        >
          {{ t('settings.diff.confirm') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
