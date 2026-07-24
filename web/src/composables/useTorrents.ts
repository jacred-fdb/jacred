import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { useDebounceFn } from '@vueuse/core'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { useApiKeyGate } from '@/composables/useApiKeyGate'
import {
  isTypingTarget,
  useKeyboardShortcut,
} from '@/composables/useKeyboardShortcut'
import { useShellTools } from '@/composables/useShellTools'
import type { AppLocale } from '@/i18n'
import { apiClient, ApiError } from '@/lib/api/client'
import { pushRecentSearch } from '@/lib/recent-searches'
import {
  getItem,
  removeItem,
  setItem,
  StorageKeys,
} from '@/lib/storage'
import {
  applyClientFilters,
  buildFacets,
  countActiveFilters,
  EMPTY_FILTERS,
  normalizeSortParam,
  pluralResults,
  SORT_API_MAP,
  sortItems,
  type SearchFilters,
  type SortValue,
  type TorrentItem,
  URL_FILTER_KEYS,
} from '@/lib/torrents'

const SEARCH_TIMEOUT_MS = 15_000

type TorrentsKey = {
  search: string
  sort: SortValue
  exact: boolean
  type: string
  tracker: string
  voice: string
  videotype: string
  year: string
  quality: string
  season: string
}

function mapSearchError(err: unknown, t: (key: string, values?: Record<string, unknown>) => string) {
  if (err instanceof ApiError) {
    if (err.status === 401) return t('search.errors.apiKeyCheck')
    if (err.status === 403) return t('search.errors.forbidden')
    if (err.status === 429) return t('search.errors.tooManyRequests')
    return t('search.errors.requestFailedStatus', { status: err.status })
  }
  if (err instanceof Error && err.name === 'AbortError') {
    return t('search.errors.timeout')
  }
  if (err instanceof Error && err.message === 'Failed to fetch') {
    return t('search.errors.network')
  }
  if (err instanceof Error && err.message) return err.message
  return t('search.errors.requestFailed')
}

/**
 * Search page state: query/sort/filters synced to the URL, TanStack Query fetch,
 * client-side facet filtering, and `/` focus shortcut.
 */
export function useTorrents() {
  const route = useRoute()
  const router = useRouter()
  const queryClient = useQueryClient()
  const { t, locale } = useI18n()
  const shell = useShellTools()
  const { ensureApiKey } = useApiKeyGate()

  const query = ref('')
  const sort = ref<SortValue>('sid')
  const exact = ref(false)
  const listView = ref(false)
  const filtersOpen = ref(getItem(StorageKeys.filtersOpen) === '1')
  const filters = ref<SearchFilters>({ ...EMPTY_FILTERS })
  const currentQuery = ref('')
  const resultsEl = ref<HTMLElement | null>(null)
  const activeKey = ref<TorrentsKey | null>(null)
  let internalRoute = ''

  const torrentsQuery = useQuery({
    queryKey: computed(() => ['torrents', activeKey.value] as const),
    enabled: computed(() => !!activeKey.value?.search),
    staleTime: 60_000,
    gcTime: 5 * 60_000,
    retry: 1,
    queryFn: async ({ signal, queryKey }): Promise<TorrentItem[]> => {
      const key = queryKey[1]
      if (!key?.search) return []
      await ensureApiKey()
      const items = await apiClient.getTorrents(
        {
          search: key.search,
          sort: SORT_API_MAP[key.sort],
          exact: key.exact ? true : undefined,
          type: key.type || undefined,
          tracker: key.tracker || undefined,
          voice: key.voice || undefined,
          videotype: key.videotype || undefined,
          relased: key.year || undefined,
          quality: key.quality || undefined,
          season: key.season || undefined,
        },
        { timeoutMs: SEARCH_TIMEOUT_MS, signal },
      )
      return Array.isArray(items) ? items : []
    },
  })

  const allItems = computed(() => torrentsQuery.data.value ?? [])
  /** First load only — keep previous rows mounted while refetching (avoids scroll jump). */
  const isLoading = computed(() => torrentsQuery.isLoading.value)
  const isFetching = computed(() => torrentsQuery.isFetching.value)
  const errorMessage = computed(() => {
    if (!torrentsQuery.isError.value || !torrentsQuery.error.value) return ''
    return mapSearchError(torrentsQuery.error.value, t)
  })

  const facets = computed(() => buildFacets(allItems.value))

  const filteredItems = computed(() =>
    sortItems(
      applyClientFilters(
        allItems.value,
        filters.value.refine,
        filters.value.exclude,
      ),
      sort.value,
    ),
  )

  /** All filtered rows — TanStack Virtual owns DOM windowing. */
  const visibleItems = filteredItems

  const activeFilterCount = computed(() => countActiveFilters(filters.value))

  const resultsHeader = computed(() => {
    const total = filteredItems.value.length
    if (!currentQuery.value || isLoading.value) return ''
    if (!total) return t('search.nothingFound')
    const lang = (locale.value === 'en' ? 'en' : 'ru') as AppLocale
    return pluralResults(total, lang)
  })

  function buildKey(search: string): TorrentsKey {
    const f = filters.value
    return {
      search,
      sort: sort.value,
      exact: exact.value,
      type: f.type,
      tracker: f.tracker,
      voice: f.voice,
      videotype: f.videotype,
      year: f.year,
      quality: f.quality,
      season: f.season,
    }
  }

  function syncUrl() {
    const q = currentQuery.value || query.value.trim()
    const params: Record<string, string> = {}
    if (q) params.s = q
    if (sort.value !== 'sid') params.sort = sort.value
    if (exact.value) params.exact = '1'
    if (listView.value) params.view = 'list'
    for (const key of URL_FILTER_KEYS) {
      const val = filters.value[key]
      if (val) params[key] = val
    }
    internalRoute = router.resolve({ path: '/', query: params }).fullPath
    void router.replace({ path: '/', query: params })
  }

  function readBootState({ initial = false } = {}) {
    const qp = route.query
    const shareQuery = [qp.text, qp.title, qp.url]
      .map((v) => (typeof v === 'string' ? v.trim() : ''))
      .find((v) => v.length > 0)
    const hasUrlSearch =
      (qp.s != null && String(qp.s).length > 0) || !!shareQuery

    for (const key of URL_FILTER_KEYS) {
      const raw = qp[key]
      if (typeof raw === 'string' && raw) {
        filters.value[key] = raw
      }
    }
    if (countActiveFilters(filters.value) > 0) {
      filtersOpen.value = true
    }

    if (typeof qp.sort === 'string') {
      const s = normalizeSortParam(qp.sort)
      if (s) sort.value = s
    } else if (initial && !hasUrlSearch) {
      const s = normalizeSortParam(getItem(StorageKeys.sort))
      if (s) sort.value = s
    }

    if (qp.exact != null) {
      exact.value = String(qp.exact) === '1'
    } else if (initial && !hasUrlSearch) {
      exact.value = getItem(StorageKeys.exact) === '1'
    }

    if (typeof qp.view === 'string') {
      listView.value = qp.view === 'list'
    } else if (initial && !hasUrlSearch) {
      listView.value = getItem(StorageKeys.listView) === '1'
    } else {
      listView.value = false
    }

    if (shareQuery) {
      query.value = shareQuery
    } else if (qp.s != null && String(qp.s).length > 0) {
      query.value = String(qp.s).trim()
    } else if (initial) {
      const saved = (getItem(StorageKeys.search) ?? '').trim()
      if (saved) query.value = saved
    }

    return hasUrlSearch
  }

  function activateSearch(q: string) {
    const trimmed = q.trim()
    if (!trimmed) return false
    setItem(StorageKeys.search, trimmed)
    currentQuery.value = trimmed
    activeKey.value = buildKey(trimmed)
    pushRecentSearch(trimmed)
    syncUrl()
    return true
  }

  async function search() {
    const q = query.value.trim()
    if (!q) {
      // Surface empty-query as a transient toast; keep query error channel for API.
      toast.error(t('search.emptyQuery'), { id: 'search-empty-query' })
      return
    }
    activateSearch(q)
  }

  function prefetchRecent(q: string) {
    const trimmed = q.trim()
    if (!trimmed) return
    const key = buildKey(trimmed)
    void queryClient.prefetchQuery({
      queryKey: ['torrents', key],
      staleTime: 60_000,
      queryFn: async ({ signal }) => {
        await ensureApiKey()
        const items = await apiClient.getTorrents(
          {
            search: key.search,
            sort: SORT_API_MAP[key.sort],
            exact: key.exact ? true : undefined,
          },
          { timeoutMs: SEARCH_TIMEOUT_MS, signal },
        )
        return Array.isArray(items) ? items : []
      },
    })
  }

  function setSort(value: SortValue) {
    sort.value = value
    setItem(StorageKeys.sort, value)
    if (query.value.trim()) void search()
  }

  function setExact(value: boolean) {
    exact.value = value
    setItem(StorageKeys.exact, value ? '1' : '0')
    if (query.value.trim()) void search()
  }

  function toggleListView() {
    listView.value = !listView.value
    setItem(StorageKeys.listView, listView.value ? '1' : '0')
    syncUrl()
  }

  function toggleFilters() {
    filtersOpen.value = !filtersOpen.value
    setItem(StorageKeys.filtersOpen, filtersOpen.value ? '1' : '0')
  }

  function setFiltersOpen(value: boolean) {
    filtersOpen.value = value
    setItem(StorageKeys.filtersOpen, value ? '1' : '0')
  }

  function updateServerFilter<K extends keyof SearchFilters>(
    key: K,
    value: SearchFilters[K],
  ) {
    filters.value = { ...filters.value, [key]: value }
    if (query.value.trim()) void search()
    else syncUrl()
  }

  const applyClientFilterDebounced = useDebounceFn(() => {
    if (currentQuery.value) syncUrl()
  }, 200)

  function updateClientFilter(key: 'refine' | 'exclude', value: string) {
    filters.value = { ...filters.value, [key]: value }
    applyClientFilterDebounced()
  }

  function resetFilters() {
    filters.value = { ...EMPTY_FILTERS }
    if (query.value.trim()) void search()
    else syncUrl()
  }

  function toggleTrackerFilter(tracker: string) {
    const current = filters.value.tracker
    const next =
      current && current.toLowerCase() === tracker.toLowerCase()
        ? ''
        : tracker
    updateServerFilter('tracker', next)
  }

  function clearSearch() {
    query.value = ''
    currentQuery.value = ''
    activeKey.value = null
    void queryClient.removeQueries({ queryKey: ['torrents'] })
    removeItem(StorageKeys.search)
    removeItem(StorageKeys.sort)
    removeItem(StorageKeys.exact)
    sort.value = 'sid'
    exact.value = false
    filters.value = { ...EMPTY_FILTERS }
    void router.replace({ path: '/' })
  }

  function onApiKeySaved() {
    toast.success(t('search.apiKeySaved'))
    if (query.value.trim()) void search()
  }

  shell.onApiKeySaved(onApiKeySaved)

  function onKeydown(e: KeyboardEvent) {
    if (e.key === '/' && !isTypingTarget(e.target)) {
      e.preventDefault()
      document.getElementById('search-input')?.focus()
      return
    }
    if (e.key === 'Escape') {
      if (shell.anyDialogOpen()) return
      if (filtersOpen.value) {
        filtersOpen.value = false
        setItem(StorageKeys.filtersOpen, '0')
        document.getElementById('search-filters-trigger')?.focus()
      }
    }
  }

  onMounted(() => {
    const shouldAutoSearch = readBootState({ initial: true })
    if (shouldAutoSearch) void search()
  })

  useKeyboardShortcut(onKeydown)

  watch(
    () => route.fullPath,
    (fullPath) => {
      if (fullPath === internalRoute) {
        internalRoute = ''
        return
      }
      filters.value = { ...EMPTY_FILTERS }
      query.value = ''
      currentQuery.value = ''
      activeKey.value = null
      sort.value = 'sid'
      exact.value = false
      listView.value = false
      const shouldSearch = readBootState()
      if (shouldSearch) void search()
    },
  )

  return {
    query,
    sort,
    exact,
    listView,
    filtersOpen,
    filters,
    facets,
    allItems,
    filteredItems,
    visibleItems,
    isLoading,
    isFetching,
    errorMessage,
    currentQuery,
    resultsEl,
    activeFilterCount,
    resultsHeader,
    search,
    prefetchRecent,
    setSort,
    setExact,
    toggleListView,
    toggleFilters,
    setFiltersOpen,
    updateServerFilter,
    updateClientFilter,
    resetFilters,
    toggleTrackerFilter,
    clearSearch,
    syncUrl,
  }
}
