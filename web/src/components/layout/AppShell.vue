<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import {
  BarChart3,
  Keyboard,
  KeyRound,
  Moon,
  Search,
  Server,
  Settings,
  Shield,
  Sun,
} from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Toaster } from '@/components/ui/sonner'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import AppFooter from '@/components/layout/AppFooter.vue'
import ScrollToTopButton from '@/components/layout/ScrollToTopButton.vue'
import { useTheme } from '@/composables/useTheme'
import { useLegacySwCleanup } from '@/composables/useLegacySwCleanup'
import { useHealth } from '@/composables/useHealth'
import { usePageTitle } from '@/composables/usePageTitle'
import { useShellTools } from '@/composables/useShellTools'
import {
  persistLocale,
  type AppLocale,
} from '@/i18n'
import { cn } from '@/lib/utils'

const ApiKeyDialog = defineAsyncComponent(
  () => import('@/components/search/ApiKeyDialog.vue'),
)
const ShortcutsDialog = defineAsyncComponent(
  () => import('@/components/search/ShortcutsDialog.vue'),
)
const TorrServerDialog = defineAsyncComponent(
  () => import('@/components/search/TorrServerDialog.vue'),
)
const DevKeyDialog = defineAsyncComponent(
  () => import('@/components/settings/DevKeyDialog.vue'),
)

useLegacySwCleanup()
usePageTitle()
const route = useRoute()
const { t, locale } = useI18n()
const { isDark, toggleTheme } = useTheme()
const { isOnline, isLoading } = useHealth()
const shell = useShellTools()
const {
  apiKeyOpen,
  devKeyOpen,
  torrServerOpen,
  shortcutsOpen,
  openApiKey,
  openDevKey,
  openTorrServer,
  openShortcuts,
  notifyApiKeySaved,
  notifyDevKeySaved,
} = shell

const nav = computed(() => [
  { to: '/', label: t('nav.search'), icon: Search, name: 'search' },
  { to: '/stats', label: t('nav.stats'), icon: BarChart3, name: 'stats' },
  {
    to: '/settings',
    label: t('nav.settings'),
    icon: Settings,
    name: 'settings',
  },
])

const tools = computed(() => [
  {
    id: 'api',
    label: t('search.apiKey'),
    icon: KeyRound,
    action: openApiKey,
  },
  {
    id: 'dev',
    label: t('settings.devKey'),
    icon: Shield,
    action: openDevKey,
  },
  {
    id: 'torr',
    label: t('search.torrServer'),
    icon: Server,
    action: openTorrServer,
  },
  {
    id: 'keys',
    label: t('search.shortcuts'),
    icon: Keyboard,
    action: openShortcuts,
  },
])

function isActive(name: string) {
  return route.name === name
}

function setLocale(next: AppLocale) {
  locale.value = next
  persistLocale(next)
  const titleKey = route.meta.titleKey as string | undefined
  if (titleKey) document.title = t(titleKey)
}

function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const tag = target.tagName
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true
  return target.isContentEditable
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === '?' && !isTypingTarget(e.target)) {
    e.preventDefault()
    openShortcuts()
  }
}

const headerRef = ref<HTMLElement | null>(null)
let headerResizeObserver: ResizeObserver | null = null

function syncHeaderOffset() {
  const height = headerRef.value?.offsetHeight ?? 0
  if (height > 0) {
    document.documentElement.style.setProperty(
      '--jr-header-offset',
      `${height}px`,
    )
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown)
  if (headerRef.value && typeof ResizeObserver !== 'undefined') {
    headerResizeObserver = new ResizeObserver(syncHeaderOffset)
    headerResizeObserver.observe(headerRef.value)
    syncHeaderOffset()
  } else {
    syncHeaderOffset()
  }
})

onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown)
  headerResizeObserver?.disconnect()
  headerResizeObserver = null
  document.documentElement.style.removeProperty('--jr-header-offset')
})
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <div class="flex min-h-dvh flex-col">
      <a
        href="#main-content"
        class="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-md focus:bg-primary focus:px-3 focus:py-2 focus:text-primary-foreground"
      >
        {{ t('app.skipToContent') }}
      </a>
      <header
        ref="headerRef"
        class="jr-glass-nav sticky top-0 z-40 border-b"
        style="padding-top: env(safe-area-inset-top)"
      >
        <div
          class="mx-auto flex h-14 max-w-6xl items-center gap-2 pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))] sm:gap-3 sm:pl-[max(1.5rem,env(safe-area-inset-left))] sm:pr-[max(1.5rem,env(safe-area-inset-right))]"
        >
          <RouterLink
            to="/"
            class="flex shrink-0 items-center gap-2 text-foreground no-underline"
          >
            <img
              src="/img/icon-32.png"
              alt=""
              width="28"
              height="28"
              class="rounded-md"
            />
            <span class="text-base font-semibold tracking-tight">{{
              t('app.name')
            }}</span>
          </RouterLink>

          <nav class="ml-1 hidden items-center gap-0.5 sm:flex">
            <Button
              v-for="item in nav"
              :key="item.to"
              as-child
              variant="ghost"
              size="sm"
              :class="
                cn(isActive(item.name) && 'bg-accent text-accent-foreground')
              "
            >
              <RouterLink
                :to="item.to"
                class="gap-1.5"
                :aria-current="isActive(item.name) ? 'page' : undefined"
              >
                <component :is="item.icon" class="size-4" />
                {{ item.label }}
              </RouterLink>
            </Button>
          </nav>

          <div class="ml-auto flex items-center gap-1 sm:gap-1.5">
            <div class="flex items-center gap-0.5">
              <Tooltip v-for="tool in tools" :key="tool.id">
                <TooltipTrigger as-child>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    class="size-8"
                    :aria-label="tool.label"
                    @click="tool.action"
                  >
                    <component :is="tool.icon" class="size-4" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>{{ tool.label }}</TooltipContent>
              </Tooltip>
            </div>

            <Tooltip>
              <TooltipTrigger as-child>
                <span
                  role="status"
                  :aria-busy="isLoading"
                  :aria-label="
                    isLoading
                      ? t('app.checking')
                      : isOnline
                        ? t('app.online')
                        : t('app.offline')
                  "
                  :class="
                    cn(
                      'jr-status-pill ml-0.5',
                      isLoading
                        ? 'jr-status-pill--loading'
                        : isOnline
                          ? 'jr-status-pill--online'
                          : 'jr-status-pill--offline',
                    )
                  "
                >
                  <span
                    aria-hidden="true"
                    :class="
                      cn(
                        'jr-status-dot',
                        isLoading
                          ? 'jr-status-dot--loading'
                          : isOnline
                            ? 'jr-status-dot--online'
                            : 'jr-status-dot--offline',
                      )
                    "
                  />
                  <span class="hidden sm:inline">{{
                    isLoading
                      ? t('app.checking')
                      : isOnline
                        ? t('app.online')
                        : t('app.offline')
                  }}</span>
                </span>
              </TooltipTrigger>
              <TooltipContent>
                {{
                  isLoading
                    ? t('app.checking')
                    : isOnline
                      ? t('app.online')
                      : t('app.offline')
                }}
              </TooltipContent>
            </Tooltip>
            <div
              class="hidden items-center rounded-md border border-[var(--jr-glass-border)] bg-[var(--jr-glass-panel)] p-0.5 sm:flex"
              role="group"
              :aria-label="`${t('app.langRu')} / ${t('app.langEn')}`"
            >
              <Button
                type="button"
                size="sm"
                :variant="locale === 'ru' ? 'secondary' : 'ghost'"
                class="h-7 px-2 text-xs"
                @click="setLocale('ru')"
              >
                {{ t('app.langRu') }}
              </Button>
              <Button
                type="button"
                size="sm"
                :variant="locale === 'en' ? 'secondary' : 'ghost'"
                class="h-7 px-2 text-xs"
                @click="setLocale('en')"
              >
                {{ t('app.langEn') }}
              </Button>
            </div>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              class="sm:hidden"
              :aria-label="locale === 'ru' ? t('app.langEn') : t('app.langRu')"
              @click="setLocale(locale === 'ru' ? 'en' : 'ru')"
            >
              <span class="text-xs font-semibold">{{
                locale === 'ru' ? t('app.langEn') : t('app.langRu')
              }}</span>
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              :aria-label="isDark ? t('app.themeLight') : t('app.themeDark')"
              @click="toggleTheme"
            >
              <Sun v-if="isDark" class="size-4" />
              <Moon v-else class="size-4" />
            </Button>
          </div>
        </div>

        <nav
          data-mobile-nav
          class="flex border-t border-[var(--jr-glass-border)] sm:hidden"
          :aria-label="t('nav.mobile')"
        >
          <RouterLink
            v-for="item in nav"
            :key="item.to"
            :to="item.to"
            :aria-current="isActive(item.name) ? 'page' : undefined"
            :class="
              cn(
                'flex flex-1 flex-col items-center gap-0.5 py-2 text-xs no-underline transition-[transform,background-color,color] duration-100 active:scale-[0.97] motion-reduce:active:scale-100',
                isActive(item.name)
                  ? 'bg-accent/60 text-accent-foreground'
                  : 'text-muted-foreground',
              )
            "
          >
            <component :is="item.icon" class="size-4" />
            {{ item.label }}
          </RouterLink>
        </nav>
      </header>

      <main
        id="main-content"
        tabindex="-1"
        :class="
          cn(
            'mx-auto w-full flex-1 py-5 outline-none pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))] sm:pl-[max(1.5rem,env(safe-area-inset-left))] sm:pr-[max(1.5rem,env(safe-area-inset-right))]',
            route.name === 'stats' ? 'max-w-7xl' : 'max-w-6xl',
          )
        "
      >
        <RouterView v-slot="{ Component, route: viewRoute }">
          <KeepAlive include="SearchPage">
            <component :is="Component" :key="viewRoute.name" />
          </KeepAlive>
        </RouterView>
      </main>

      <AppFooter />

      <Toaster rich-colors close-button position="top-center" />
      <ScrollToTopButton />

      <ApiKeyDialog v-model:open="apiKeyOpen" @saved="notifyApiKeySaved" />
      <DevKeyDialog v-model:open="devKeyOpen" @saved="notifyDevKeySaved" />
      <TorrServerDialog v-model:open="torrServerOpen" />
      <ShortcutsDialog v-model:open="shortcutsOpen" />
    </div>
  </TooltipProvider>
</template>
