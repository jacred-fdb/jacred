<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { Server } from '@lucide/vue'
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
import {
  getTorrServerLogin,
  getTorrServerPassword,
  getTorrServerUrl,
  setTorrServerCreds,
} from '@/lib/storage'

const open = defineModel<boolean>('open', { default: false })
const { t } = useI18n()

const url = ref('')
const login = ref('')
const password = ref('')
const error = ref('')

watch(open, (v) => {
  if (v) {
    url.value = getTorrServerUrl()
    login.value = getTorrServerLogin()
    password.value = getTorrServerPassword()
    error.value = ''
  }
})

function save() {
  const base = url.value.trim()
  if (!base) {
    error.value = t('search.torrServerDialog.urlRequired')
    return
  }
  try {
    new URL(base)
  } catch {
    error.value = t('search.torrServerDialog.invalidUrl')
    return
  }
  setTorrServerCreds(base, login.value.trim(), password.value)
  open.value = false
  toast.success(t('search.torrServerDialog.saved'))
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2">
          <Server class="size-4" />
          TorrServer
        </DialogTitle>
        <DialogDescription>
          {{ t('search.torrServerDialog.description') }}
        </DialogDescription>
      </DialogHeader>
      <form class="space-y-3" @submit.prevent="save">
        <label for="torrserver-url" class="text-sm font-medium">
          {{ t('search.torrServerDialog.urlLabel') }}
        </label>
        <Input
          id="torrserver-url"
          v-model="url"
          type="url"
          placeholder="http://127.0.0.1:8090"
          :aria-label="t('search.torrServerDialog.urlAria')"
        />
        <label for="torrserver-login" class="text-sm font-medium">
          {{ t('search.torrServerDialog.loginLabel') }}
        </label>
        <Input
          id="torrserver-login"
          v-model="login"
          type="text"
          autocomplete="username"
          :placeholder="t('search.torrServerDialog.loginPlaceholder')"
          :aria-label="t('search.torrServerDialog.loginAria')"
        />
        <label for="torrserver-password" class="text-sm font-medium">
          {{ t('search.torrServerDialog.passwordLabel') }}
        </label>
        <Input
          id="torrserver-password"
          v-model="password"
          type="password"
          autocomplete="current-password"
          :placeholder="t('search.torrServerDialog.passwordPlaceholder')"
          :aria-label="t('search.torrServerDialog.passwordAria')"
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
