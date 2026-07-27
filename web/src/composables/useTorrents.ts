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
import {
  type ApiMode,
  applyV2ClientFilters,
  buildJackettSearchQuery,
  categoriesToQueryParam,
  countActiveV2Filters,
  EMPTY_V2_FILTERS,
  mapJackettResults,
  normalizeApiMode,
  parseCategoriesParam,
  parseQualitiesParam,
  parseTrackersParam,
  type V2SearchFilters,
} from '@/lib/jackett'
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
  splitTrackerNames,
  type SearchFilters,
  type SortValue,
  type TorrentItem,
  URL_FILTER_KEYS,
} from '@/lib/torrents'

const SEARCH_TIMEOUT_MS = 15_000

type TorrentsKeyV1 = {
  apiMode: 'v1'
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

type TorrentsKeyV2 = {
  apiMode: 'v2'
  search: string
  title: string
  titleOriginal: string
  year: string
  isSerial: string
  categories: string
}

type TorrentsKey = TorrentsKeyV1 | TorrentsKeyV2

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

async function fetchTorrentsForKey(
  key: TorrentsKey,
  signal: AbortSignal | undefined,
  ensureApiKey: () => Promise<void>,
): Promise<TorrentItem[]> {
  await ensureApiKey()
  if (key.apiMode === 'v2') {
    const query = buildJackettSearchQuery(key.search, {
      title: key.title,
      titleOriginal: key.titleOriginal,
      year: key.year,
      isSerial: key.isSerial,
      categories: key.categories ? key.categories.split(',') : [],
    })
    const root = await apiClient.getJackettResults('all', query, {
      timeoutMs: SEARCH_TIMEOUT_MS,
      signal,
    })
    return mapJackettResults(root)
  }
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
  const apiMode = ref<ApiMode>('v1')
  const listView = ref(false)
  const filtersOpen = ref(getItem(StorageKeys.filtersOpen) === '1')
  const filters = ref<SearchFilters>({ ...EMPTY_FILTERS })
  const v2Filters = ref<V2SearchFilters>({ ...EMPTY_V2_FILTERS })
  const currentQuery = ref('')
  const activeKey = ref<TorrentsKey | null>(null)
  let internalRoute = ''

  const torrentsQuery = useQuery({
    queryKey: computed(() => ['torrents', activeKey.value] as const),
    enabled: computed(() => !!activeKey.value?.search),
    staleTime: 60_000,
    gcTime: 5 * 60_000,
    retry: 1,
    // Keep prior rows only for same search (sort/filter). New query → no placeholder.
    placeholderData: (
      previousData: TorrentItem[] | undefined,
      previousQuery,
    ): TorrentItem[] | undefined => {
      const prevKey = previousQuery?.queryKey?.[1] as TorrentsKey | null | undefined
      const nextKey = activeKey.value
      if (!previousData || !prevKey?.search || !nextKey?.search) return undefined
      if (prevKey.search !== nextKey.search) return undefined
      if (prevKey.apiMode !== nextKey.apiMode) return undefined
      return previousData
    },
    queryFn: async ({ signal, queryKey }): Promise<TorrentItem[]> => {
      const key = queryKey[1]
      if (!key?.search) return []
      return fetchTorrentsForKey(key, signal, ensureApiKey)
    },
  })

  const rawItems = computed(() => torrentsQuery.data.value ?? [])
  const allItems = computed(() => {
    if (apiMode.value === 'v2') return sortItems(rawItems.value, sort.value)
    return rawItems.value
  })
  /** Keep previous rows while refetching (avoids scroll jump). */
  const isLoading = computed(() => torrentsQuery.isLoading.value)
  const isFetching = computed(() => torrentsQuery.isFetching.value)
  const errorMessage = computed(() => {
    if (!torrentsQuery.isError.value || !torrentsQuery.error.value) return ''
    return mapSearchError(torrentsQuery.error.value, t)
  })

  const facets = computed(() =>
    buildFacets(allItems.value, { splitTrackers: apiMode.value === 'v2' }),
  )

  /** Debounced refine/exclude so typing doesn't filter every keystroke. */
  const appliedClient = ref({
    refine: '',
    exclude: '',
    trackers: [] as string[],
    qualities: [] as string[],
    voices: [] as string[],
    seasons: [] as string[],
    langs: [] as string[],
    videotype: '',
  })

  function syncAppliedClient() {
    if (apiMode.value === 'v2') {
      const f = v2Filters.value
      appliedClient.value = {
        refine: f.refine,
        exclude: f.exclude,
        trackers: [...f.trackers],
        qualities: [...f.qualities],
        voices: [...f.voices],
        seasons: [...f.seasons],
        langs: [...f.langs],
        videotype: f.videotype,
      }
      return
    }
    appliedClient.value = {
      refine: filters.value.refine,
      exclude: filters.value.exclude,
      trackers: [],
      qualities: [],
      voices: [],
      seasons: [],
      langs: [],
      videotype: '',
    }
  }

  const filteredItems = computed(() => {
    if (apiMode.value === 'v2') {
      return applyV2ClientFilters(allItems.value, appliedClient.value)
    }
    return applyClientFilters(
      allItems.value,
      appliedClient.value.refine,
      appliedClient.value.exclude,
    )
  })

  /** Filtered rows; off-screen paint via CSS content-visibility. */
  const visibleItems = filteredItems

  const activeFilterCount = computed(() =>
    apiMode.value === 'v2'
      ? countActiveV2Filters(v2Filters.value)
      : countActiveFilters(filters.value),
  )

  const resultsHeader = computed(() => {
    const total = filteredItems.value.length
    if (!currentQuery.value || isLoading.value) return ''
    // Empty copy lives in the dashed panel — avoid duplicate «Nothing found».
    if (!total) return ''
    const lang = (locale.value === 'en' ? 'en' : 'ru') as AppLocale
    return pluralResults(total, lang)
  })

  function buildKey(search: string): TorrentsKey {
    if (apiMode.value === 'v2') {
      const f = v2Filters.value
      return {
        apiMode: 'v2',
        search,
        title: f.title,
        titleOriginal: f.titleOriginal,
        year: f.year,
        isSerial: f.isSerial,
        categories: categoriesToQueryParam(f.categories),
      }
    }
    const f = filters.value
    return {
      apiMode: 'v1',
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
    if (listView.value) params.view = 'list'
    if (apiMode.value === 'v2') {
      params.api = 'v2'
      const f = v2Filters.value
      if (f.title) params.jtitle = f.title
      if (f.titleOriginal) params.joriginal = f.titleOriginal
      if (f.year) params.year = f.year
      if (f.isSerial) params.is_serial = f.isSerial
      const cat = categoriesToQueryParam(f.categories)
      if (cat) params.cat = cat
      const trackers = categoriesToQueryParam(f.trackers)
      if (trackers) params.tracker = trackers
      const qlt = categoriesToQueryParam(f.qualities)
      if (qlt) params.qlt = qlt
      const voices = categoriesToQueryParam(f.voices)
      if (voices) params.voice = voices
      const seasons = categoriesToQueryParam(f.seasons)
      if (seasons) params.season = seasons
      const langs = categoriesToQueryParam(f.langs)
      if (langs) params.lang = langs
      if (f.videotype) params.videotype = f.videotype
      if (f.refine) params.refine = f.refine
      if (f.exclude) params.exclude = f.exclude
    } else {
      if (exact.value) params.exact = '1'
      for (const key of URL_FILTER_KEYS) {
        const val = filters.value[key]
        if (val) params[key] = val
      }
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

    if (typeof qp.api === 'string') {
      apiMode.value = normalizeApiMode(qp.api)
    } else if (initial && !hasUrlSearch) {
      apiMode.value = normalizeApiMode(getItem(StorageKeys.apiMode))
    } else if (!qp.api) {
      apiMode.value = 'v1'
    }

    filters.value = { ...EMPTY_FILTERS }
    v2Filters.value = { ...EMPTY_V2_FILTERS }

    if (apiMode.value === 'v2') {
      if (typeof qp.jtitle === 'string' && qp.jtitle) {
        v2Filters.value.title = qp.jtitle
      }
      if (typeof qp.joriginal === 'string' && qp.joriginal) {
        v2Filters.value.titleOriginal = qp.joriginal
      }
      if (typeof qp.year === 'string' && qp.year) {
        v2Filters.value.year = qp.year
      }
      if (typeof qp.is_serial === 'string' && qp.is_serial) {
        v2Filters.value.isSerial = qp.is_serial
      }
      if (typeof qp.cat === 'string' && qp.cat) {
        v2Filters.value.categories = parseCategoriesParam(qp.cat)
      }
      if (typeof qp.tracker === 'string' && qp.tracker) {
        v2Filters.value.trackers = parseTrackersParam(qp.tracker)
      }
      if (typeof qp.qlt === 'string' && qp.qlt) {
        v2Filters.value.qualities = parseQualitiesParam(qp.qlt)
      }
      if (typeof qp.voice === 'string' && qp.voice) {
        v2Filters.value.voices = parseTrackersParam(qp.voice)
      }
      if (typeof qp.season === 'string' && qp.season) {
        v2Filters.value.seasons = parseTrackersParam(qp.season)
      }
      if (typeof qp.lang === 'string' && qp.lang) {
        v2Filters.value.langs = parseTrackersParam(qp.lang)
      }
      if (typeof qp.videotype === 'string' && qp.videotype) {
        v2Filters.value.videotype = qp.videotype
      }
      if (typeof qp.refine === 'string' && qp.refine) {
        v2Filters.value.refine = qp.refine
      }
      if (typeof qp.exclude === 'string' && qp.exclude) {
        v2Filters.value.exclude = qp.exclude
      }
    } else {
      for (const key of URL_FILTER_KEYS) {
        const raw = qp[key]
        if (typeof raw === 'string' && raw) {
          filters.value[key] = raw
        }
      }
    }

    if (activeFilterCount.value > 0) {
      filtersOpen.value = true
    }
    syncAppliedClient()

    if (typeof qp.sort === 'string') {
      const s = normalizeSortParam(qp.sort)
      if (s) sort.value = s
    } else if (initial && !hasUrlSearch) {
      const s = normalizeSortParam(getItem(StorageKeys.sort))
      if (s) sort.value = s
    }

    if (apiMode.value === 'v1') {
      if (qp.exact != null) {
        exact.value = String(qp.exact) === '1'
      } else if (initial && !hasUrlSearch) {
        exact.value = getItem(StorageKeys.exact) === '1'
      }
    } else {
      exact.value = false
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
      queryFn: async ({ signal }) =>
        fetchTorrentsForKey(key, signal, ensureApiKey),
    })
  }

  function setSort(value: SortValue) {
    sort.value = value
    setItem(StorageKeys.sort, value)
    if (apiMode.value === 'v2') {
      syncUrl()
      return
    }
    if (query.value.trim()) void search()
  }

  function setExact(value: boolean) {
    exact.value = value
    setItem(StorageKeys.exact, value ? '1' : '0')
    if (query.value.trim()) void search()
  }

  function setApiMode(mode: ApiMode) {
    if (mode === apiMode.value) return
    apiMode.value = mode
    setItem(StorageKeys.apiMode, mode)
    if (mode === 'v2') {
      filters.value = { ...EMPTY_FILTERS }
      exact.value = false
    } else {
      v2Filters.value = { ...EMPTY_V2_FILTERS }
    }
    syncAppliedClient()
    if (query.value.trim()) void search()
    else syncUrl()
  }

  function toggleListView() {
    listView.value = !listView.value
    setItem(StorageKeys.listView, listView.value ? '1' : '0')
    syncUrl()
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

  const applyV2TextFilterDebounced = useDebounceFn(() => {
    if (query.value.trim()) void search()
    else syncUrl()
  }, 320)

  function updateV2Filter<K extends keyof V2SearchFilters>(
    key: K,
    value: V2SearchFilters[K],
  ) {
    v2Filters.value = { ...v2Filters.value, [key]: value }
    const clientKeys: (keyof V2SearchFilters)[] = [
      'refine',
      'exclude',
      'trackers',
      'qualities',
      'voices',
      'seasons',
      'langs',
      'videotype',
    ]
    if (clientKeys.includes(key)) {
      applyClientFilterDebounced()
      return
    }
    if (key === 'title' || key === 'titleOriginal' || key === 'year') {
      applyV2TextFilterDebounced()
      return
    }
    if (query.value.trim()) void search()
    else syncUrl()
  }

  function toggleV2Category(category: string) {
    const current = v2Filters.value.categories
    const next = current.includes(category)
      ? current.filter((c) => c !== category)
      : [...current, category]
    updateV2Filter('categories', next)
  }

  function toggleV2ListFilter(
    key: 'trackers' | 'qualities' | 'voices' | 'seasons' | 'langs',
    value: string,
  ) {
    const name = value.trim()
    if (!name) return
    const current = v2Filters.value[key]
    const exists = current.some((t) => t.toLowerCase() === name.toLowerCase())
    const next = exists
      ? current.filter((t) => t.toLowerCase() !== name.toLowerCase())
      : [...current, name]
    updateV2Filter(key, next)
  }

  function toggleV2Tracker(tracker: string) {
    toggleV2ListFilter('trackers', tracker)
  }

  const applyClientFilterDebounced = useDebounceFn(() => {
    syncAppliedClient()
    if (currentQuery.value) syncUrl()
  }, 180)

  function updateClientFilter(key: 'refine' | 'exclude', value: string) {
    if (apiMode.value === 'v2') {
      v2Filters.value = { ...v2Filters.value, [key]: value }
    } else {
      filters.value = { ...filters.value, [key]: value }
    }
    applyClientFilterDebounced()
  }

  function resetFilters() {
    if (apiMode.value === 'v2') {
      v2Filters.value = { ...EMPTY_V2_FILTERS }
    } else {
      filters.value = { ...EMPTY_FILTERS }
    }
    syncAppliedClient()
    if (query.value.trim()) void search()
    else syncUrl()
  }

  function toggleTrackerFilter(tracker: string) {
    const names = splitTrackerNames(tracker)
    if (!names.length) return
    if (apiMode.value === 'v2') {
      const current = v2Filters.value.trackers
      const selected = new Set(current.map((t) => t.toLowerCase()))
      const allOn = names.every((n) => selected.has(n.toLowerCase()))
      let next: string[]
      if (allOn) {
        next = current.filter(
          (t) => !names.some((n) => n.toLowerCase() === t.toLowerCase()),
        )
      } else {
        next = [...current]
        for (const n of names) {
          if (!next.some((t) => t.toLowerCase() === n.toLowerCase())) {
            next.push(n)
          }
        }
      }
      updateV2Filter('trackers', next)
      return
    }
    const primary = names[0]!
    const current = filters.value.tracker
    const next =
      current && current.toLowerCase() === primary.toLowerCase() ? '' : primary
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
    v2Filters.value = { ...EMPTY_V2_FILTERS }
    syncAppliedClient()
    // Keep listView + apiMode preference (storage + in-memory) across clear.
    void router.replace({
      path: '/',
      query: {
        ...(listView.value ? { view: 'list' } : {}),
        ...(apiMode.value === 'v2' ? { api: 'v2' } : {}),
      },
    })
  }

  function retrySearch() {
    if (query.value.trim()) void search()
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
      v2Filters.value = { ...EMPTY_V2_FILTERS }
      query.value = ''
      currentQuery.value = ''
      activeKey.value = null
      sort.value = 'sid'
      exact.value = false
      // Preserve listView / apiMode from URL/storage via readBootState.
      const shouldSearch = readBootState()
      if (shouldSearch) void search()
    },
  )

  return {
    query,
    sort,
    exact,
    apiMode,
    listView,
    filtersOpen,
    filters,
    v2Filters,
    facets,
    allItems,
    filteredItems,
    visibleItems,
    isLoading,
    isFetching,
    errorMessage,
    currentQuery,
    activeFilterCount,
    resultsHeader,
    search,
    retrySearch,
    prefetchRecent,
    setSort,
    setExact,
    setApiMode,
    toggleListView,
    setFiltersOpen,
    updateServerFilter,
    updateV2Filter,
    toggleV2Category,
    toggleV2Tracker,
    toggleV2ListFilter,
    updateClientFilter,
    resetFilters,
    toggleTrackerFilter,
    clearSearch,
    syncUrl,
  }
}
