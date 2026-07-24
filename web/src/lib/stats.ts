export type TrackerTracks = {
  wait: number
  confirm: number
  skip: number
}

export type TrackerStat = {
  trackerName: string
  lastnewtor?: string | null
  newtor?: number | null
  update?: number | null
  check?: number | null
  alltorrents?: number | null
  tracks?: Partial<TrackerTracks> | null
}

export type StatsMeta = {
  ok?: boolean
  updatedAt?: string | null
  updatedAtLocal?: string | null
  tracksStatsUpdatedAt?: string | null
}

export type StatsSort =
  | 'name'
  | 'newtor'
  | 'update'
  | 'alltorrents'
  | 'confirm'
  | 'wait'
  | 'skip'

export type StatsView = 'table' | 'cards'

export const STATS_PAGE_SIZE = 50
export const STATS_CARD_PAGE_SIZE = 24
export const STATS_VIEW_BREAKPOINT = 768

export const TRACKER_LABELS: Record<string, string> = {
  anidub: 'AniDub',
  aniliberty: 'AniLiberty',
  animelayer: 'AnimeLayer',
  baibako: 'Baibako',
  bitru: 'BitRu',
  hdrezka: 'HDRezka',
  kinozal: 'Kinozal',
  knaben: 'Knaben',
  lostfilm: 'LostFilm',
  mazepa: 'Mazepa',
  megapeer: 'Megapeer',
  nnmclub: 'NNM-Club',
  rutor: 'RuTor',
  rutracker: 'RuTracker',
  selezen: 'Selezen',
  toloka: 'Toloka',
  torrentby: 'Torrent.by',
}

export const STATS_SORT_OPTIONS: { value: StatsSort; label: string }[] = [
  { value: 'name', label: 'По имени' },
  { value: 'newtor', label: 'Новые' },
  { value: 'update', label: 'Обновлено' },
  { value: 'alltorrents', label: 'Всего' },
  { value: 'confirm', label: 'Подтверждено' },
  { value: 'wait', label: 'Ожидает' },
  { value: 'skip', label: 'Пропущено' },
]

export function getTrackerDisplayName(slug: string | null | undefined): string {
  const key = String(slug || '').toLowerCase()
  if (!key) return '—'
  return TRACKER_LABELS[key] || key.charAt(0).toUpperCase() + key.slice(1)
}

export function getTracksData(item: TrackerStat): TrackerTracks {
  return {
    wait: Number(item.tracks?.wait) || 0,
    confirm: Number(item.tracks?.confirm) || 0,
    skip: Number(item.tracks?.skip) || 0,
  }
}

export function formatStatNumber(
  n: number | null | undefined,
  full: boolean,
  locale = 'ru-RU',
): string {
  const num = Number(n)
  if (n == null || Number.isNaN(num)) return '0'
  if (!full) {
    return new Intl.NumberFormat(locale, {
      notation: 'compact',
      maximumFractionDigits: 1,
    }).format(num)
  }
  return num.toLocaleString(locale)
}

export function formatStatNumberFull(
  n: number | null | undefined,
  locale = 'ru-RU',
): string {
  const num = Number(n)
  if (n == null || Number.isNaN(num)) return '0'
  return num.toLocaleString(locale)
}

export function formatStatsUpdatedAt(
  value: string | null | undefined,
  locale = 'ru-RU',
): string {
  if (!value) return ''
  const trimmed = value.trim()
  if (!trimmed) return ''
  // Already localized like 23.07.2026 12:07
  if (/^\d{1,2}\.\d{1,2}\.\d{4}/.test(trimmed)) {
    return trimmed.replace(/:\d{2}$/, '').replace(/,?\s+/g, ' ').trim()
  }
  const parsed = Date.parse(trimmed)
  if (Number.isNaN(parsed)) return trimmed
  return new Date(parsed).toLocaleString(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function getSortKey(item: TrackerStat, sort: StatsSort): string | number {
  const tracks = getTracksData(item)
  switch (sort) {
    case 'name':
      return (item.trackerName || '').toLowerCase()
    case 'newtor':
      return Number(item.newtor) || 0
    case 'update':
      return Number(item.update) || 0
    case 'alltorrents':
      return Number(item.alltorrents) || 0
    case 'confirm':
      return tracks.confirm
    case 'wait':
      return tracks.wait
    case 'skip':
      return tracks.skip
    default:
      return 0
  }
}

export function filterAndSortTrackers(
  data: TrackerStat[],
  query: string,
  sort: StatsSort,
): TrackerStat[] {
  const q = query.toLowerCase().trim()
  const list = data.filter((item) => {
    if (!q) return true
    const slug = (item.trackerName || '').toLowerCase()
    const label = getTrackerDisplayName(slug).toLowerCase()
    return slug.includes(q) || label.includes(q)
  })

  list.sort((a, b) => {
    const ka = getSortKey(a, sort)
    const kb = getSortKey(b, sort)
    if (sort === 'name') {
      if (ka < kb) return -1
      if (ka > kb) return 1
      return 0
    }
    return (kb as number) - (ka as number)
  })

  return list
}

export type StatsAggregate = {
  newtor: number
  update: number
  alltorrents: number
  confirm: number
  wait: number
  skip: number
  count: number
}

export function aggregateTrackers(list: TrackerStat[]): StatsAggregate {
  const agg: StatsAggregate = {
    newtor: 0,
    update: 0,
    alltorrents: 0,
    confirm: 0,
    wait: 0,
    skip: 0,
    count: list.length,
  }
  for (const item of list) {
    const t = getTracksData(item)
    agg.newtor += Number(item.newtor) || 0
    agg.update += Number(item.update) || 0
    agg.alltorrents += Number(item.alltorrents) || 0
    agg.confirm += t.confirm
    agg.wait += t.wait
    agg.skip += t.skip
  }
  return agg
}

export function pluralTrackers(n: number): string {
  const abs = Math.abs(n | 0)
  const mod10 = abs % 10
  const mod100 = abs % 100
  if (mod10 === 1 && mod100 !== 11) return `${n} трекер`
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) {
    return `${n} трекера`
  }
  return `${n} трекеров`
}

export function isDesktopViewport(): boolean {
  return window.matchMedia(`(min-width: ${STATS_VIEW_BREAKPOINT}px)`).matches
}

export function normalizeStatsSort(
  value: string | null | undefined,
): StatsSort | '' {
  const v = String(value || '')
  if (
    v === 'name' ||
    v === 'newtor' ||
    v === 'update' ||
    v === 'alltorrents' ||
    v === 'confirm' ||
    v === 'wait' ||
    v === 'skip'
  ) {
    return v
  }
  return ''
}
