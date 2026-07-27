import type { TorrentItem } from '@/lib/torrents'
import { splitTrackerNames } from '@/lib/torrents'

/** Native v1 vs Jackett-compatible API v2 search mode. */
export type ApiMode = 'v1' | 'v2'

export function normalizeApiMode(val: string | null | undefined): ApiMode {
  return val === 'v2' ? 'v2' : 'v1'
}

/** Runtime Jackett/JacRed result (OpenAPI schema is a subset). */
export type JackettResult = {
  Tracker?: string | null
  Title?: string | null
  Size?: number | null
  Seeders?: number | null
  Peers?: number | null
  MagnetUri?: string | null
  Details?: string | null
  PublishDate?: string | null
  languages?: string[] | null
  info?: {
    quality?: number | null
    videotype?: string | null
    voices?: string[] | null
    seasons?: number[] | null
    types?: string[] | null
    sizeName?: string | null
    name?: string | null
    originalname?: string | null
    relased?: number | null
  } | null
}

/**
 * Jackett search filters.
 * Server params: title/year/is_serial/categories.
 * Client multi (Lampa-style): trackers/qualities/voices/seasons/langs + videotype.
 */
export type V2SearchFilters = {
  title: string
  titleOriginal: string
  year: string
  isSerial: string
  categories: string[]
  /** Lampa multi: individual trackers — client-side (keeps full facet list) */
  trackers: string[]
  /** Lampa multi: 4k / 1080p / 720p — client-side */
  qualities: string[]
  /** Lampa multi: studio/voice names from results — client-side */
  voices: string[]
  /** Lampa multi: season numbers from results — client-side */
  seasons: string[]
  /** Lampa multi: language codes from results — client-side */
  langs: string[]
  /** Lampa-style HDR single: '' | sdr | hdr — client-side */
  videotype: string
  refine: string
  exclude: string
}

export const EMPTY_V2_FILTERS: V2SearchFilters = {
  title: '',
  titleOriginal: '',
  year: '',
  isSerial: '',
  categories: [],
  trackers: [],
  qualities: [],
  voices: [],
  seasons: [],
  langs: [],
  videotype: '',
  refine: '',
  exclude: '',
}

/** URL query keys for v2 filters (avoid clashing with share-target `title`). */
export const URL_V2_FILTER_KEYS = [
  'jtitle',
  'joriginal',
  'year',
  'is_serial',
  'cat',
  'tracker',
  'qlt',
  'voice',
  'season',
  'lang',
  'videotype',
  'refine',
  'exclude',
] as const

export const V2_CATEGORY_OPTIONS = [
  { value: '2000', labelKey: 'search.filters.catMovies' },
  { value: '5000', labelKey: 'search.filters.catTv' },
  { value: '5070', labelKey: 'search.filters.catAnime' },
] as const

/** Lampa quality multi-select options (client-side title / info.quality match). */
export const V2_QUALITY_OPTIONS = [
  { value: '4k', labelKey: 'search.filters.quality4k' },
  { value: '1080p', labelKey: 'search.filters.quality1080' },
  { value: '720p', labelKey: 'search.filters.quality720' },
] as const

export const V2_IS_SERIAL_OPTIONS = [
  { value: '', labelKey: 'search.filters.all' },
  { value: '1', labelKey: 'search.filters.isSerialMovie' },
  { value: '2', labelKey: 'search.filters.isSerialSerial' },
  { value: '3', labelKey: 'search.filters.isSerialTvshow' },
  { value: '4', labelKey: 'search.filters.isSerialDoc' },
  { value: '5', labelKey: 'search.filters.isSerialAnime' },
] as const

export function formatByteSize(bytes: number | null | undefined): string {
  const n = Number(bytes)
  if (!Number.isFinite(n) || n <= 0) return ''
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let value = n
  let i = 0
  while (value >= 1024 && i < units.length - 1) {
    value /= 1024
    i += 1
  }
  const digits = i === 0 ? 0 : value >= 10 ? 1 : 2
  return `${value.toFixed(digits)} ${units[i]}`
}

export function mapJackettResult(r: JackettResult): TorrentItem {
  const info = r.info
  const size = r.Size != null ? Number(r.Size) : null
  const sizeName =
    info?.sizeName ||
    (size != null && Number.isFinite(size) ? formatByteSize(size) : null)
  const languages = Array.isArray(r.languages)
    ? r.languages.map((l) => String(l).trim()).filter(Boolean)
    : null
  return {
    tracker: r.Tracker ?? null,
    title: r.Title ?? null,
    size: size != null && Number.isFinite(size) ? size : null,
    sizeName,
    url: r.Details ?? null,
    createTime: r.PublishDate ?? null,
    updateTime: r.PublishDate ?? null,
    sid: r.Seeders ?? null,
    pir: r.Peers ?? null,
    magnet: r.MagnetUri ?? null,
    name: info?.name ?? null,
    originalname: info?.originalname ?? null,
    relased: info?.relased ?? null,
    videotype: info?.videotype ?? null,
    quality: info?.quality ?? null,
    voices: info?.voices ?? null,
    seasons: info?.seasons ?? null,
    types: info?.types ?? null,
    languages,
  }
}

export function mapJackettResults(
  root: { Results?: JackettResult[] | null } | null | undefined,
): TorrentItem[] {
  const list = root?.Results
  if (!Array.isArray(list)) return []
  return list.map(mapJackettResult)
}

export function countActiveV2Filters(f: V2SearchFilters): number {
  let n = 0
  if (f.title) n += 1
  if (f.titleOriginal) n += 1
  if (f.year) n += 1
  if (f.isSerial) n += 1
  if (f.categories.length) n += 1
  if (f.trackers.length) n += 1
  if (f.qualities.length) n += 1
  if (f.voices.length) n += 1
  if (f.seasons.length) n += 1
  if (f.langs.length) n += 1
  if (f.videotype) n += 1
  if (f.refine) n += 1
  if (f.exclude) n += 1
  return n
}

export function categoriesToQueryParam(values: string[]): string {
  return values
    .map((c) => c.trim())
    .filter(Boolean)
    .sort()
    .join(',')
}

export function parseCategoriesParam(raw: string | null | undefined): string[] {
  if (!raw) return []
  const allowed = new Set(V2_CATEGORY_OPTIONS.map((o) => o.value))
  return raw
    .split(',')
    .map((c) => c.trim())
    .filter((c) => allowed.has(c as (typeof V2_CATEGORY_OPTIONS)[number]['value']))
}

export function parseQualitiesParam(raw: string | null | undefined): string[] {
  if (!raw) return []
  const allowed = new Set(V2_QUALITY_OPTIONS.map((o) => o.value))
  return raw
    .split(',')
    .map((c) => c.trim().toLowerCase())
    .filter((c) => allowed.has(c as (typeof V2_QUALITY_OPTIONS)[number]['value']))
}

/** Parse URL list (comma-separated, de-duped, order preserved). */
export function parseTrackersParam(raw: string | null | undefined): string[] {
  if (!raw) return []
  const seen = new Set<string>()
  const out: string[] = []
  for (const part of raw.split(',')) {
    const name = part.trim()
    if (!name) continue
    const key = name.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    out.push(name)
  }
  return out
}

export type JackettSearchQuery = {
  query?: string
  title?: string
  title_original?: string
  year?: number
  is_serial?: number
  'Category[]'?: string[]
}

export type V2ServerFilters = Pick<
  V2SearchFilters,
  'title' | 'titleOriginal' | 'year' | 'isSerial' | 'categories'
>

export function buildJackettSearchQuery(
  search: string,
  filters: V2ServerFilters,
): JackettSearchQuery {
  const yearNum = Number(filters.year)
  return {
    query: search || undefined,
    title: filters.title || undefined,
    title_original: filters.titleOriginal || undefined,
    year:
      filters.year && Number.isFinite(yearNum) && yearNum > 0
        ? yearNum
        : undefined,
    is_serial: filters.isSerial ? Number(filters.isSerial) : undefined,
    'Category[]':
      filters.categories.length > 0 ? [...filters.categories] : undefined,
  }
}

const QUALITY_TITLE_RE: Record<string, RegExp> = {
  '4k': /(4k|uhd)[ \]|,|$]|2160[pр]|ultrahd/i,
  '1080p': /fullhd|1080[pр]/i,
  '720p': /720[pр]/i,
}

function qualityMatches(item: TorrentItem, label: string, title: string): boolean {
  const q = Number(item.quality)
  if (Number.isFinite(q) && q > 0) {
    if (label === '4k' && q >= 2160) return true
    if (label === '1080p' && q >= 1080 && q < 2160) return true
    if (label === '720p' && q >= 720 && q < 1080) return true
  }
  const re = QUALITY_TITLE_RE[label]
  return re ? re.test(title) : false
}

function voiceMatches(item: TorrentItem, voice: string, title: string): boolean {
  const needle = voice.toLowerCase()
  if (item.voices?.some((v) => String(v).toLowerCase() === needle)) return true
  return title.toLowerCase().includes(needle)
}

function seasonMatches(item: TorrentItem, season: string): boolean {
  const n = Number(season)
  if (!Number.isFinite(n)) return false
  return (item.seasons ?? []).some((s) => Number(s) === n)
}

function langMatches(item: TorrentItem, lang: string, title: string): boolean {
  const code = lang.toLowerCase().slice(0, 2)
  if (!code) return false
  if (item.languages?.some((l) => String(l).toLowerCase().slice(0, 2) === code)) {
    return true
  }
  return title.toLowerCase().includes(code)
}

function trackerMatches(item: TorrentItem, tracker: string): boolean {
  const want = tracker.toLowerCase()
  return splitTrackerNames(item.tracker).some((t) => t.toLowerCase() === want)
}

function videotypeMatches(item: TorrentItem, videotype: string, title: string): boolean {
  const want = videotype.toLowerCase()
  if (!want) return true
  const raw = (item.videotype || '').toLowerCase()
  if (raw) return raw === want
  const hasHdr = /[[| ]hdr(10)?[ |\],$]/i.test(title)
  if (want === 'hdr') return hasHdr
  if (want === 'sdr') return !hasHdr
  return true
}

/**
 * Lampa-style client filters for Jackett results.
 * Within each multi group: OR. Across groups: AND.
 */
export function applyV2ClientFilters(
  items: TorrentItem[],
  filters: Pick<
    V2SearchFilters,
    | 'trackers'
    | 'qualities'
    | 'voices'
    | 'seasons'
    | 'langs'
    | 'videotype'
    | 'refine'
    | 'exclude'
  >,
): TorrentItem[] {
  const refine = filters.refine.trim().toLowerCase()
  const exclude = filters.exclude.trim().toLowerCase()
  const hasMulti =
    filters.trackers.length > 0 ||
    filters.qualities.length > 0 ||
    filters.voices.length > 0 ||
    filters.seasons.length > 0 ||
    filters.langs.length > 0 ||
    !!filters.videotype ||
    !!refine ||
    !!exclude
  if (!hasMulti) return items

  return items.filter((el) => {
    const title = el.title || el.name || ''
    const titleLower = title.toLowerCase()
    if (refine && !titleLower.includes(refine)) return false
    if (exclude && titleLower.includes(exclude)) return false

    if (filters.trackers.length) {
      const ok = filters.trackers.some((t) => trackerMatches(el, t))
      if (!ok) return false
    }
    if (filters.qualities.length) {
      const ok = filters.qualities.some((q) => qualityMatches(el, q, title))
      if (!ok) return false
    }
    if (filters.voices.length) {
      const ok = filters.voices.some((v) => voiceMatches(el, v, title))
      if (!ok) return false
    }
    if (filters.seasons.length) {
      const ok = filters.seasons.some((s) => seasonMatches(el, s))
      if (!ok) return false
    }
    if (filters.langs.length) {
      const ok = filters.langs.some((l) => langMatches(el, l, title))
      if (!ok) return false
    }
    if (filters.videotype && !videotypeMatches(el, filters.videotype, title)) {
      return false
    }
    return true
  })
}
