<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { KeyRound } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { getDevKey, setDevKey } from '@/lib/storage'

const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()
const { t } = useI18n()

const value = ref('')
const error = ref('')

watch(open, (v) => {
  if (v) {
    value.value = getDevKey()
    error.value = ''
  }
})

function save() {
  const key = value.value.trim()
  if (!key) {
    error.value = t('settings.devKeyDialog.required')
    return
  }
  setDevKey(key)
  open.value = false
  emit('saved')
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2">
          <KeyRound class="size-4" />
          {{ t('settings.devKeyDialog.title') }}
        </DialogTitle>
        <DialogDescription>
          {{ t('settings.devKeyDialog.description') }}
        </DialogDescription>
      </DialogHeader>
      <form class="space-y-3" @submit.prevent="save">
        <label for="dev-key" class="text-sm font-medium">
          {{ t('settings.devKeyDialog.label') }}
        </label>
        <Input
          id="dev-key"
          v-model="value"
          type="password"
          autocomplete="new-password"
          :placeholder="t('settings.devKeyDialog.label')"
        />
        <p v-if="error" class="text-sm text-destructive" role="alert">
          {{ error }}
        </p>
        <DialogFooter>
          <Button type="button" variant="outline" @click="open = false">
            {{ t('app.cancel') }}
          </Button>
          <Button type="submit">{{ t('app.save') }}</Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
