<script setup lang="ts">
import { defineAsyncComponent } from 'vue'
import {
  FileCode2,
  KeyRound,
  Loader2,
  RefreshCw,
  Save,
  Sparkles,
  ShieldAlert,
} from '@lucide/vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import SettingsDiffDialog from '@/components/settings/SettingsDiffDialog.vue'
import SettingsForm from '@/components/settings/SettingsForm.vue'
import { useConfig } from '@/composables/useConfig'
import {
  formatMetaDate,
  type ConfigFormat,
} from '@/lib/config-schema'
import { segmentItem, segmentTrackChrome } from '@/lib/segment-classes'

const { t, locale } = useI18n()
const SettingsRawEditor = defineAsyncComponent(
  () => import('@/components/settings/SettingsRawEditor.vue'),
)

const {
  mode,
  format,
  schema,
  formData,
  rawContent,
  path,
  lastModifiedUtc,
  activeTab,
  isLoading,
  isBusy,
  dirty,
  accessDenied,
  accessMessage,
  accessKind,
  validation,
  errorMessage,
  hasEditor,
  diffDialogOpen,
  pendingDiff,
  markDirty,
  updateField,
  setActiveTab,
  switchMode,
  onFormatChange,
  validate,
  formatConfig,
  prepareSave,
  confirmSave,
  reload,
  openApiKey,
  openDevKey,
} = useConfig()
</script>

<template>
  <section class="space-y-4">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div class="space-y-1">
        <h1 class="text-2xl font-semibold tracking-tight">
          {{ t('settings.title') }}
        </h1>
        <p class="text-sm text-muted-foreground">
          {{ t('settings.subtitle') }}
        </p>
      </div>
      <Button
        type="button"
        variant="outline"
        size="sm"
        class="h-9 gap-1.5"
        :disabled="isLoading || isBusy"
        @click="reload"
      >
        <RefreshCw class="size-3.5" />
        {{ t('settings.reload') }}
      </Button>
    </div>

    <div
      v-if="accessDenied"
      class="jr-elevated space-y-3 rounded-xl border border-destructive/30 p-5"
    >
      <div class="flex items-start gap-3">
        <ShieldAlert class="mt-0.5 size-5 text-destructive" />
        <div class="space-y-1">
          <h2 class="font-semibold">{{ t('settings.accessDenied') }}</h2>
          <p class="text-sm text-muted-foreground">{{ accessMessage }}</p>
        </div>
      </div>
      <Button
        v-if="accessKind === 'devkey' || accessKind === 'apikey'"
        type="button"
        class="gap-1.5"
        @click="accessKind === 'apikey' ? openApiKey() : openDevKey()"
      >
        <KeyRound class="size-4" />
        {{
          accessKind === 'apikey'
            ? t('search.apiKey')
            : t('settings.enterDevKey')
        }}
      </Button>
    </div>

    <div
      v-else-if="isLoading && !hasEditor"
      class="space-y-3"
      aria-busy="true"
      :aria-label="t('settings.loading')"
    >
      <div class="flex flex-wrap items-center gap-2">
        <div class="h-6 w-28 animate-pulse rounded-full bg-muted/70" />
        <div class="h-6 w-14 animate-pulse rounded-full bg-muted/70" />
        <div class="h-6 w-36 animate-pulse rounded-full bg-muted/70" />
      </div>
      <div
        class="flex h-12 animate-pulse flex-wrap items-center gap-2 rounded-lg border bg-muted/40 px-2"
      >
        <div class="h-8 w-28 rounded-md bg-muted-foreground/15" />
        <div class="h-8 w-20 rounded-md bg-muted-foreground/15" />
        <div class="ml-auto h-8 w-16 rounded-md bg-muted-foreground/15" />
        <div class="h-8 w-20 rounded-md bg-primary/20" />
      </div>
      <div class="flex h-auto w-full flex-wrap gap-1 rounded-[10px] bg-secondary p-0.5">
        <div
          v-for="i in 8"
          :key="i"
          class="h-8 w-24 animate-pulse rounded-[8px] bg-muted-foreground/10"
        />
      </div>
      <div class="jr-elevated animate-pulse space-y-4 rounded-xl border p-4">
        <div class="h-5 w-48 rounded bg-muted-foreground/20" />
        <div class="grid gap-3 sm:grid-cols-2">
          <div class="space-y-1.5">
            <div class="h-3 w-24 rounded bg-muted-foreground/15" />
            <div class="h-9 rounded-md bg-muted-foreground/10" />
          </div>
          <div class="space-y-1.5">
            <div class="h-3 w-16 rounded bg-muted-foreground/15" />
            <div class="h-9 rounded-md bg-muted-foreground/10" />
          </div>
        </div>
        <div class="space-y-1.5">
          <div class="h-3 w-20 rounded bg-muted-foreground/15" />
          <div class="h-9 rounded-md bg-muted-foreground/10" />
        </div>
        <div class="space-y-1.5">
          <div class="h-3 w-24 rounded bg-muted-foreground/15" />
          <div class="h-9 rounded-md bg-muted-foreground/10" />
        </div>
      </div>
    </div>

    <div
      v-else-if="errorMessage"
      class="jr-elevated space-y-3 rounded-xl border border-dashed px-4 py-12 text-center"
      role="alert"
    >
      <p class="text-sm text-destructive">{{ errorMessage }}</p>
      <Button
        type="button"
        variant="outline"
        size="sm"
        class="h-9 gap-1.5"
        :disabled="isLoading || isBusy"
        @click="reload"
      >
        <RefreshCw class="size-3.5" />
        {{ t('settings.reload') }}
      </Button>
    </div>

    <template v-else-if="hasEditor && schema">
      <div class="flex flex-wrap items-center gap-2 text-sm">
        <Badge v-if="path" variant="secondary" class="gap-1 font-normal">
          <FileCode2 class="size-3.5" />
          <code>{{ path }}</code>
        </Badge>
        <Badge variant="outline">{{ String(format).toUpperCase() }}</Badge>
        <Badge v-if="lastModifiedUtc" variant="outline" class="font-normal">
          {{ formatMetaDate(lastModifiedUtc, locale) }}
        </Badge>
        <Badge v-if="dirty" variant="warning">{{
          t('settings.unsaved')
        }}</Badge>
      </div>

      <div
        class="jr-sticky-dock jr-settings-dock sticky flex flex-wrap items-center gap-2 py-2"
        style="top: var(--jr-header-offset)"
      >
        <Tabs
          :model-value="mode"
          @update:model-value="(v) => switchMode(v as 'form' | 'raw')"
        >
          <TabsList :class="segmentTrackChrome">
            <TabsTrigger
              value="form"
              :class="segmentItem"
            >
              {{ t('settings.form') }}
            </TabsTrigger>
            <TabsTrigger
              value="raw"
              :class="segmentItem"
            >
              {{ t('settings.raw') }}
            </TabsTrigger>
          </TabsList>
        </Tabs>

        <Select
          :model-value="format"
          @update:model-value="(v) => onFormatChange(String(v) as ConfigFormat)"
        >
          <SelectTrigger class="h-9 w-28" :aria-label="t('settings.formatAria')">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="yaml">YAML</SelectItem>
            <SelectItem value="json">JSON</SelectItem>
          </SelectContent>
        </Select>

        <div class="ml-auto flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            class="h-9"
            :disabled="isBusy"
            @click="validate"
          >
            {{ t('settings.validate') }}
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            class="h-9 gap-1.5"
            :disabled="isBusy"
            @click="formatConfig({ switchToRaw: true })"
          >
            <Sparkles class="size-3.5" />
            {{ t('settings.format') }}
          </Button>
          <Button
            type="button"
            size="sm"
            class="h-9 gap-1.5"
            :disabled="isBusy"
            @click="prepareSave"
          >
            <Loader2 v-if="isBusy" class="size-3.5 animate-spin" />
            <Save v-else class="size-3.5" />
            {{ t('settings.save') }}
          </Button>
        </div>
      </div>

      <div v-if="validation" class="space-y-1">
        <div
          v-if="validation.errors?.length"
          class="rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
        >
          <strong>{{ t('settings.errorsLabel') }}</strong>
          <ul class="mt-1 list-disc pl-4">
            <li v-for="(e, i) in validation.errors" :key="i">{{ e }}</li>
          </ul>
        </div>
        <div
          v-else-if="validation.error"
          class="rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
        >
          {{ validation.error }}
        </div>
        <div
          v-else-if="validation.ok && !validation.warnings?.length"
          class="jr-tone-success rounded-lg border px-3 py-2 text-sm"
        >
          {{ t('settings.configValid') }}
        </div>
        <div
          v-else-if="validation.warnings?.length"
          class="jr-tone-warning rounded-lg border px-3 py-2 text-sm"
        >
          <strong>{{ t('settings.warningsLabel') }}</strong>
          <ul class="mt-1 list-disc pl-4">
            <li v-for="(w, i) in validation.warnings" :key="i">{{ w }}</li>
          </ul>
        </div>
      </div>

      <p
        v-if="errorMessage"
        class="text-sm text-destructive"
        role="alert"
      >
        {{ errorMessage }}
      </p>

      <div :class="isBusy ? 'pointer-events-none opacity-60' : ''">
        <SettingsForm
          v-if="mode === 'form'"
          :schema="schema"
          :data="formData"
          :active-tab="activeTab"
          @update:active-tab="setActiveTab"
          @change="updateField($event.path, $event.value)"
        />
        <div v-else class="space-y-2">
          <p class="text-xs text-muted-foreground">
            {{ t('settings.rawHint') }}
          </p>
          <SettingsRawEditor
            v-model:content="rawContent"
            :format="format"
            :disabled="isBusy"
            @change="markDirty"
          />
        </div>
      </div>
    </template>

    <SettingsDiffDialog
      v-model:open="diffDialogOpen"
      :diff="pendingDiff"
      @confirm="confirmSave"
    />
  </section>
</template>
