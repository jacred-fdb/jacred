<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import {
  ArrowDown,
  ArrowUp,
  CalendarPlus,
  HardDrive,
  RefreshCw,
} from '@lucide/vue'
import TorrentActionRail from '@/components/search/TorrentActionRail.vue'
import {
  copyText,
  extractInfoHash,
  isSafeMagnetUrl,
  sendToTorrServer,
  TorrServerError,
} from '@/lib/magnets'
import {
  getTorrServerLogin,
  getTorrServerPassword,
  getTorrServerUrl,
} from '@/lib/storage'
import {
  formatDate,
  formatQualityLabel,
  getSafeIconPath,
  isSafeHttpUrl,
  qualityTier,
  splitTrackerNames,
  type TorrentItem,
} from '@/lib/torrents'
import { cn } from '@/lib/utils'

const props = defineProps<{
  item: TorrentItem
  listView: boolean
  position: number
  setSize: number
  activeTrackers: string[]
}>()

const emit = defineEmits<{
  filterTracker: [tracker: string]
  openTorrServer: []
}>()

const { t } = useI18n()
const torState = ref<'idle' | 'loading' | 'success'>('idle')
let torResetTimer: ReturnType<typeof setTimeout> | null = null

const title = computed(() => props.item.title || props.item.name || '')
const magnet = computed(() => {
  const value = (props.item.magnet || '').trim()
  return isSafeMagnetUrl(value) ? value : ''
})
const hasMagnet = computed(() => magnet.value.length > 0)
const safeUrl = computed(() =>
  isSafeHttpUrl(props.item.url) ? props.item.url! : null,
)
const qualityLabel = computed(() => formatQualityLabel(props.item.quality))
const tier = computed(() => qualityTier(props.item.quality))
const createStr = computed(() => formatDate(props.item.createTime))
const updateStr = computed(() =>
  props.item.updateTime ? formatDate(props.item.updateTime) : null,
)
const showUpdate = computed(
  () => !!updateStr.value && updateStr.value !== createStr.value,
)
const tracker = computed(() => props.item.tracker || '')
const isActiveTracker = computed(() => {
  if (!props.activeTrackers.length) return false
  const selected = new Set(
    props.activeTrackers.map((t) => t.toLowerCase()).filter(Boolean),
  )
  return splitTrackerNames(tracker.value).some((name) =>
    selected.has(name.toLowerCase()),
  )
})
const iconSrc = computed(() => getSafeIconPath(splitTrackerNames(tracker.value)[0] || tracker.value))

const qualityClass = computed(() => {
  switch (tier.value) {
    case '4k':
      return 'jr-quality jr-quality--4k'
    case '1440':
      return 'jr-quality jr-quality--1440'
    case '1080':
      return 'jr-quality jr-quality--1080'
    case '720':
      return 'jr-quality jr-quality--720'
    case 'sd':
      return 'jr-quality jr-quality--sd'
    default:
      return 'jr-quality jr-quality--unknown'
  }
})

const titleNode = computed(() => (safeUrl.value ? 'a' : 'span'))
const titleBind = computed(() =>
  safeUrl.value
    ? { href: safeUrl.value, target: '_blank', rel: 'noopener noreferrer' }
    : {},
)

onUnmounted(() => {
  if (torResetTimer) clearTimeout(torResetTimer)
})

async function onCopyMagnet() {
  if (!hasMagnet.value) return
  try {
    await copyText(magnet.value)
    toast.success(t('search.card.copied'))
  } catch {
    toast.error(t('search.card.copyFailed'))
  }
}

async function onCopyHash() {
  const hash = extractInfoHash(magnet.value)
  if (!hash) return
  try {
    await copyText(hash)
    toast.success(t('search.card.hashCopied'))
  } catch {
    toast.error(t('search.card.copyFailed'))
  }
}

async function onSendTorr() {
  if (!hasMagnet.value || torState.value === 'loading') return
  const baseUrl = getTorrServerUrl().trim()
  if (!baseUrl) {
    emit('openTorrServer')
    return
  }
  torState.value = 'loading'
  try {
    await sendToTorrServer(magnet.value, {
      baseUrl,
      login: getTorrServerLogin().trim(),
      password: getTorrServerPassword().trim(),
    })
    toast.success(t('search.card.torrSent'))
    torState.value = 'success'
    if (torResetTimer) clearTimeout(torResetTimer)
    torResetTimer = setTimeout(() => {
      torState.value = 'idle'
      torResetTimer = null
    }, 1800)
  } catch (err) {
    toast.error(
      err instanceof TorrServerError
        ? t(`search.card.torrErrors.${err.code}`, { status: err.status ?? 0 })
        : t('search.card.torrError'),
    )
    torState.value = 'idle'
  }
}
</script>

<template>
  <article
    data-result-card
    role="listitem"
    :aria-posinset="position"
    :aria-setsize="setSize"
    :data-layout="listView ? 'list' : 'card'"
    :class="
      cn(
        'jr-elevated border transition-colors hover:border-primary/40',
        'focus-within:ring-1 focus-within:ring-ring/40',
        listView
          ? 'flex flex-col gap-1 rounded-md px-2 py-1.5 sm:flex-row sm:items-center sm:gap-2 sm:py-1'
          : 'jr-result-card flex flex-col gap-0 rounded-lg p-0 sm:gap-1.5 sm:p-2',
      )
    "
  >
    <template v-if="listView">
      <div class="flex min-w-0 flex-1 items-start gap-2 sm:items-center">
        <button
          type="button"
          :class="
            cn(
              'jr-tracker-filter relative inline-flex size-8 shrink-0 items-center justify-center rounded-md transition-[transform,background-color,color] duration-100 active:scale-[0.97] motion-reduce:active:scale-100',
              isActiveTracker
                ? 'bg-primary/20 text-primary ring-1 ring-primary/30'
                : 'text-muted-foreground hover:bg-muted hover:text-foreground',
            )
          "
          :title="t('search.card.filterTracker', { tracker })"
          :aria-label="t('search.card.filterTracker', { tracker })"
          @click.stop="emit('filterTracker', tracker)"
        >
          <img
            :src="iconSrc"
            alt=""
            width="20"
            height="20"
            class="size-5 rounded-sm"
            loading="lazy"
            @error="($event.target as HTMLImageElement).style.visibility = 'hidden'"
          />
        </button>

        <component
          :is="titleNode"
          v-bind="titleBind"
          class="block min-w-0 flex-1 text-[0.8125rem] leading-snug font-medium text-foreground no-underline line-clamp-2 hover:text-primary sm:text-sm sm:line-clamp-1"
          :title="title"
        >
          {{ title || t('search.card.untitled') }}
        </component>
      </div>

      <div class="jr-result-meta jr-result-meta--list">
        <span
          v-if="qualityLabel"
          :class="cn(qualityClass, 'jr-result-meta__quality')"
        >
          {{ qualityLabel }}
        </span>
        <span
          class="jr-meta-chip jr-meta-chip--size jr-result-meta__size"
          :title="t('search.card.size')"
        >
          <HardDrive class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ item.sizeName || '—' }}</span>
        </span>
        <span
          class="jr-meta-chip jr-meta-chip--seeds jr-result-meta__seeds"
          :title="t('search.card.seeds')"
        >
          <ArrowUp class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ item.sid ?? 0 }}</span>
        </span>
        <span
          class="jr-meta-chip jr-meta-chip--peers jr-result-meta__peers"
          :title="t('search.card.peers')"
        >
          <ArrowDown class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ item.pir ?? 0 }}</span>
        </span>
        <span
          class="jr-meta-chip jr-meta-chip--added jr-result-meta__added"
          :title="t('search.card.added')"
        >
          <CalendarPlus class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ createStr }}</span>
        </span>
        <span
          v-if="showUpdate"
          class="jr-meta-chip jr-meta-chip--updated jr-result-meta__updated"
          :title="t('search.card.updated')"
        >
          <RefreshCw class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ updateStr }}</span>
        </span>
      </div>

      <div class="jr-result-actions--list w-full sm:w-auto sm:shrink-0">
        <TorrentActionRail
          :magnet="magnet"
          :has-magnet="hasMagnet"
          :tor-state="torState"
          @copy-magnet="onCopyMagnet"
          @copy-hash="onCopyHash"
          @send-torr="onSendTorr"
        />
      </div>
    </template>

    <template v-else>
      <div
        class="jr-result-card__head flex items-start gap-2 px-2.5 pt-2 pb-1.5 sm:p-0"
      >
        <button
          type="button"
          :class="
            cn(
              'jr-tracker-filter relative mt-0.5 inline-flex size-8 shrink-0 items-center justify-center rounded-md transition-[transform,background-color,color] duration-100 active:scale-[0.97] motion-reduce:active:scale-100',
              isActiveTracker
                ? 'bg-primary/20 text-primary ring-1 ring-primary/30'
                : 'text-muted-foreground hover:bg-muted hover:text-foreground',
            )
          "
          :title="t('search.card.filterTracker', { tracker })"
          :aria-label="t('search.card.filterTracker', { tracker })"
          @click.stop="emit('filterTracker', tracker)"
        >
          <img
            :src="iconSrc"
            alt=""
            width="22"
            height="22"
            class="size-[22px] rounded-sm"
            loading="lazy"
            @error="($event.target as HTMLImageElement).style.visibility = 'hidden'"
          />
        </button>
        <component
          :is="titleNode"
          v-bind="titleBind"
          class="min-w-0 flex-1 text-[0.95rem] leading-snug font-semibold text-foreground no-underline line-clamp-2 hover:text-primary"
          :title="title"
        >
          {{ title || t('search.card.untitled') }}
        </component>
        <span v-if="qualityLabel" :class="cn(qualityClass, 'mt-0.5')">{{
          qualityLabel
        }}</span>
      </div>

      <div class="jr-result-card__meta jr-result-meta">
        <span
          class="jr-meta-chip jr-meta-chip--size"
          :title="t('search.card.size')"
        >
          <HardDrive class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ item.sizeName || '—' }}</span>
        </span>
        <span
          class="jr-meta-chip jr-meta-chip--seeds"
          :title="t('search.card.seeds')"
        >
          <ArrowUp class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ item.sid ?? 0 }}</span>
        </span>
        <span
          class="jr-meta-chip jr-meta-chip--peers"
          :title="t('search.card.peers')"
        >
          <ArrowDown class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ item.pir ?? 0 }}</span>
        </span>
        <span
          class="jr-meta-chip jr-meta-chip--added"
          :title="t('search.card.added')"
        >
          <CalendarPlus class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ createStr }}</span>
        </span>
        <span
          v-if="showUpdate"
          class="jr-meta-chip jr-meta-chip--updated"
          :title="t('search.card.updated')"
        >
          <RefreshCw class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="jr-meta-chip__value">{{ updateStr }}</span>
        </span>
      </div>

      <div class="jr-result-card__actions w-full px-2.5 pt-1.5 pb-2 sm:p-0">
        <TorrentActionRail
          :magnet="magnet"
          :has-magnet="hasMagnet"
          :tor-state="torState"
          @copy-magnet="onCopyMagnet"
          @copy-hash="onCopyHash"
          @send-torr="onSendTorr"
        />
      </div>
    </template>
  </article>
</template>
