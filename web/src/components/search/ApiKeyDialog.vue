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
import { getApiKey, setApiKey } from '@/lib/storage'

const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()
const { t } = useI18n()

const value = ref('')
const error = ref('')

watch(open, (v) => {
  if (v) {
    value.value = getApiKey()
    error.value = ''
  }
})

function save() {
  const key = value.value.trim()
  if (!key) {
    error.value = t('search.apiKeyDialog.required')
    return
  }
  setApiKey(key)
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
          {{ t('search.apiKeyDialog.title') }}
        </DialogTitle>
        <DialogDescription>
          {{ t('search.apiKeyDialog.description') }}
        </DialogDescription>
      </DialogHeader>
      <form class="space-y-3" @submit.prevent="save">
        <label for="api-key" class="text-sm font-medium">
          {{ t('search.apiKeyDialog.label') }}
        </label>
        <Input
          id="api-key"
          v-model="value"
          type="password"
          autocomplete="off"
          :placeholder="t('search.apiKeyDialog.label')"
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
