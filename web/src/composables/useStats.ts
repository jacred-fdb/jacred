import { useDebounceFn, useMediaQuery } from '@vueuse/core'
import { useQuery } from '@tanstack/vue-query'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { apiClient, ApiError } from '@/lib/api/client'
import { useApiKeyGate } from '@/composables/useApiKeyGate'
import {
  isTypingTarget,
  useKeyboardShortcut,
} from '@/composables/useKeyboardShortcut'
import { useShellTools } from '@/composables/useShellTools'
import {
  aggregateTrackers,
  filterAndSortTrackers,
  formatStatsUpdatedAt,
  isDesktopViewport,
  normalizeStatsSort,
  STATS_CARD_PAGE_SIZE,
  STATS_PAGE_SIZE,
  STATS_VIEW_BREAKPOINT,
  type StatsMeta,
  type StatsSort,
  type StatsView,
  type TrackerStat,
} from '@/lib/stats'
import { animateResultCards } from '@/motion/staggerEnter'

const RETRY_ATTEMPTS = 3
const RETRY_DELAY_MS = 1_000

async function sleep(ms: number) {
  await new Promise((r) => window.setTimeout(r, ms))
}

export function useStats() {
  const { t, locale } = useI18n()
  const route = useRoute()
  const router = useRouter()
  const isDesktop = useMediaQuery(`(min-width: ${STATS_VIEW_BREAKPOINT}px)`)
  const shell = useShellTools()
  const { ensureApiKey } = useApiKeyGate()

  const query = ref('')
  const sort = ref<StatsSort>('name')
  const view = ref<StatsView>('table')
  const fullNumbers = ref(true)
  const wideMode = ref(false)
  const viewManuallySet = ref(false)
  const page = ref(1)

  const data = ref<TrackerStat[]>([])
  const meta = ref<StatsMeta | null>(null)
  const errorMessage = ref('')
  const gridEl = ref<HTMLElement | null>(null)
  let internalRoute = ''

  const filtered = computed(() =>
    filterAndSortTrackers(data.value, query.value, sort.value),
  )

  const aggregate = computed(() => aggregateTrackers(filtered.value))

  const pageSize = computed(() =>
    view.value === 'table' ? STATS_PAGE_SIZE : STATS_CARD_PAGE_SIZE,
  )
  const totalPages = computed(() =>
    Math.max(1, Math.ceil(filtered.value.length / pageSize.value)),
  )

  const pagedRows = computed(() => {
    const start = (page.value - 1) * pageSize.value
    return filtered.value.slice(start, start + pageSize.value)
  })

  const paginationLabel = computed(() => {
    const total = filtered.value.length
    if (total <= pageSize.value) return ''
    const start = (page.value - 1) * pageSize.value + 1
    const end = Math.min(page.value * pageSize.value, total)
    return t('stats.pagination', { start, end, total })
  })

  const showTable = computed(() => view.value === 'table')
  const showCards = computed(() => view.value === 'cards' && isDesktop.value)
  const showMobileList = computed(
    () => view.value === 'cards' && !isDesktop.value,
  )

  const counterLabel = computed(() =>
    t(
      'stats.trackerCount',
      { count: filtered.value.length },
      { plural: filtered.value.length },
    ),
  )

  const updatedLabel = computed(() => {
    const m = meta.value
    if (!m) return ''
    return formatStatsUpdatedAt(m.updatedAtLocal || m.updatedAt, locale.value)
  })

  function syncUrl() {
    const params: Record<string, string> = {}
    const q = query.value.trim()
    if (q) params.q = q
    if (sort.value !== 'name') params.sort = sort.value
    params.view = view.value
    if (!fullNumbers.value) params.numbers = 'short'
    if (wideMode.value) params.wide = '1'
    internalRoute = router.resolve({ path: '/stats', query: params }).fullPath
    void router.replace({ path: '/stats', query: params })
  }

  function readBootState() {
    const qp = route.query
    query.value = typeof qp.q === 'string' ? qp.q : ''
    sort.value = 'name'
    fullNumbers.value = true
    wideMode.value = false
    viewManuallySet.value = false
    const s = normalizeStatsSort(
      typeof qp.sort === 'string' ? qp.sort : undefined,
    )
    if (s) sort.value = s
    if (qp.view === 'table' || qp.view === 'cards') {
      view.value = qp.view
      viewManuallySet.value = true
    } else {
      view.value = isDesktopViewport() ? 'table' : 'cards'
    }
    if (qp.numbers === 'short') fullNumbers.value = false
    else if (qp.numbers === 'full') fullNumbers.value = true
    wideMode.value = qp.wide === '1'
    page.value = 1
  }

  async function fetchTorrentsWithRetry(): Promise<TrackerStat[]> {
    let lastError: Error | null = null
    for (let i = 0; i < RETRY_ATTEMPTS; i++) {
      try {
        const raw = await apiClient.getStatsTorrents()
        return Array.isArray(raw) ? raw : []
      } catch (err) {
        if (err instanceof ApiError && err.status >= 400 && err.status < 500) {
          throw err
        }
        lastError =
          err instanceof Error
            ? err
            : new Error(t('stats.errors.loadFailed'))
        if (i < RETRY_ATTEMPTS - 1) await sleep(RETRY_DELAY_MS)
      }
    }
    throw lastError ?? new Error(t('stats.errors.loadFailed'))
  }

  const statsQuery = useQuery({
    queryKey: ['stats'],
    enabled: false,
    retry: false,
    queryFn: async () => {
      await ensureApiKey()
      const items = await fetchTorrentsWithRetry()
      const statsMeta = await apiClient.getStatsMeta().catch(() => null)
      return { items, meta: statsMeta }
    },
  })
  const isLoading = computed(() => statsQuery.isFetching.value)

  async function load() {
    if (isLoading.value) return
    errorMessage.value = ''

    try {
      const result = await statsQuery.refetch()
      if (result.error) throw result.error
      data.value = result.data?.items ?? []
      meta.value = result.data?.meta ?? null
      page.value = 1
      syncUrl()
    } catch (err) {
      data.value = []
      if (err instanceof ApiError) {
        if (err.status === 401) {
          errorMessage.value = t('stats.errors.apiKeyCheck')
          shell.openApiKey()
        } else if (err.status === 403) {
          errorMessage.value = t('stats.errors.forbidden')
        } else {
          errorMessage.value = t('stats.errors.requestFailed', {
            status: err.status,
          })
        }
      } else if (err instanceof Error && err.name === 'AbortError') {
        errorMessage.value = t('stats.errors.timeout')
      } else {
        errorMessage.value =
          err instanceof Error
            ? err.message
            : t('stats.errors.loadFailed')
      }
      toast.error(errorMessage.value)
    }
  }

  const debouncedSync = useDebounceFn(() => {
    page.value = 1
    syncUrl()
  }, 300)

  function setQuery(value: string) {
    query.value = value
    debouncedSync()
  }

  function setSort(value: StatsSort) {
    sort.value = value
    page.value = 1
    syncUrl()
  }

  function setView(value: StatsView) {
    view.value = value
    viewManuallySet.value = true
    page.value = 1
    syncUrl()
  }

  function toggleNumbers() {
    fullNumbers.value = !fullNumbers.value
    syncUrl()
  }

  function toggleWide() {
    wideMode.value = !wideMode.value
    syncUrl()
  }

  function prevPage() {
    if (page.value > 1) page.value -= 1
  }

  function nextPage() {
    if (page.value < totalPages.value) page.value += 1
  }

  function onApiKeySaved() {
    toast.success(t('stats.apiKeySaved'))
    void load()
  }

  shell.onApiKeySaved(onApiKeySaved)

  function onKeydown(e: KeyboardEvent) {
    if (e.key === '/' && !isTypingTarget(e.target)) {
      e.preventDefault()
      document.getElementById('stats-search')?.focus()
    }
  }

  watch(isDesktop, (desktop) => {
    if (viewManuallySet.value) return
    view.value = desktop ? 'table' : 'cards'
  })

  watch(
    () => route.fullPath,
    (fullPath) => {
      if (fullPath === internalRoute) {
        internalRoute = ''
        return
      }
      readBootState()
    },
  )

  watch(
    () => [showCards.value, showMobileList.value, filtered.value.length] as const,
    async ([cards, list]) => {
      if (!cards && !list) return
      await Promise.resolve()
      animateResultCards(gridEl.value)
    },
  )

  onMounted(() => {
    readBootState()
    void load()
  })
  useKeyboardShortcut(onKeydown)

  return {
    query,
    sort,
    view,
    fullNumbers,
    wideMode,
    page,
    data,
    meta,
    isLoading,
    errorMessage,
    gridEl,
    filtered,
    aggregate,
    pagedRows,
    paginationLabel,
    totalPages,
    showTable,
    showCards,
    showMobileList,
    counterLabel,
    updatedLabel,
    setQuery,
    setSort,
    setView,
    toggleNumbers,
    toggleWide,
    prevPage,
    nextPage,
    load,
    syncUrl,
  }
}
