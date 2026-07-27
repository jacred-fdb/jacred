import { describe, expect, it } from 'vitest'
import {
  applyV2ClientFilters,
  buildJackettSearchQuery,
  countActiveV2Filters,
  EMPTY_V2_FILTERS,
  formatByteSize,
  mapJackettResult,
  mapJackettResults,
  normalizeApiMode,
  parseCategoriesParam,
  parseQualitiesParam,
  parseTrackersParam,
} from '@/lib/jackett'

describe('jackett helpers', () => {
  it('normalizes api mode', () => {
    expect(normalizeApiMode('v2')).toBe('v2')
    expect(normalizeApiMode('v1')).toBe('v1')
    expect(normalizeApiMode(null)).toBe('v1')
  })

  it('maps Jackett results into torrent cards', () => {
    const item = mapJackettResult({
      Tracker: 'rutor',
      Title: 'Example',
      Size: 1_073_741_824,
      Seeders: 12,
      Peers: 3,
      MagnetUri: 'magnet:?xt=urn:btih:abc',
      Details: 'https://example.test/t/1',
      PublishDate: '2026-07-23T10:00:00Z',
      languages: ['rus', 'eng'],
      info: {
        quality: 1080,
        voices: ['LostFilm'],
        seasons: [1, 2],
        sizeName: '1.00 GB',
      },
    })
    expect(item).toMatchObject({
      tracker: 'rutor',
      title: 'Example',
      size: 1_073_741_824,
      sizeName: '1.00 GB',
      sid: 12,
      pir: 3,
      magnet: 'magnet:?xt=urn:btih:abc',
      url: 'https://example.test/t/1',
      quality: 1080,
      voices: ['LostFilm'],
      seasons: [1, 2],
      languages: ['rus', 'eng'],
    })
  })

  it('formats byte sizes when info.sizeName is missing', () => {
    expect(formatByteSize(1536)).toBe('1.50 KB')
    expect(mapJackettResult({ Size: 2048 }).sizeName).toBe('2.00 KB')
  })

  it('maps empty roots safely', () => {
    expect(mapJackettResults(null)).toEqual([])
    expect(mapJackettResults({ Results: null })).toEqual([])
  })

  it('builds Jackett query params including Category[]', () => {
    expect(
      buildJackettSearchQuery('matrix', {
        title: 'Матрица',
        titleOriginal: 'The Matrix',
        year: '1999',
        isSerial: '1',
        categories: ['2000', '5070'],
      }),
    ).toEqual({
      query: 'matrix',
      title: 'Матрица',
      title_original: 'The Matrix',
      year: 1999,
      is_serial: 1,
      'Category[]': ['2000', '5070'],
    })
  })

  it('counts active v2 filters and parses list params', () => {
    expect(countActiveV2Filters(EMPTY_V2_FILTERS)).toBe(0)
    expect(
      countActiveV2Filters({
        ...EMPTY_V2_FILTERS,
        title: 'x',
        categories: ['2000'],
        trackers: ['rutor'],
        qualities: ['4k'],
        refine: 'hdr',
      }),
    ).toBe(5)
    expect(parseCategoriesParam('2000,5070,9999')).toEqual(['2000', '5070'])
    expect(parseTrackersParam('rutor, kinozal,rutor')).toEqual([
      'rutor',
      'kinozal',
    ])
    expect(parseQualitiesParam('4k,720p,bogus')).toEqual(['4k', '720p'])
  })

  it('applies Lampa-style multi client filters with OR within group', () => {
    const items = [
      mapJackettResult({
        Title: 'Show S01 2160p HDR',
        Tracker: 'rutor',
        info: { quality: 2160, voices: ['LostFilm'], seasons: [1], videotype: 'hdr' },
        languages: ['ru'],
      }),
      mapJackettResult({
        Title: 'Show S02 1080p',
        Tracker: 'kinozal',
        info: { quality: 1080, voices: ['NewStudio'], seasons: [2], videotype: 'sdr' },
        languages: ['en'],
      }),
    ]

    expect(
      applyV2ClientFilters(items, {
        ...EMPTY_V2_FILTERS,
        trackers: ['rutor', 'kinozal'],
      }).map((i) => i.title),
    ).toEqual(['Show S01 2160p HDR', 'Show S02 1080p'])

    expect(
      applyV2ClientFilters(items, {
        ...EMPTY_V2_FILTERS,
        trackers: ['rutor'],
      }).map((i) => i.title),
    ).toEqual(['Show S01 2160p HDR'])

    expect(
      applyV2ClientFilters(items, {
        ...EMPTY_V2_FILTERS,
        qualities: ['4k', '720p'],
      }).map((i) => i.title),
    ).toEqual(['Show S01 2160p HDR'])

    expect(
      applyV2ClientFilters(items, {
        ...EMPTY_V2_FILTERS,
        voices: ['NewStudio'],
      }).map((i) => i.title),
    ).toEqual(['Show S02 1080p'])

    expect(
      applyV2ClientFilters(items, {
        ...EMPTY_V2_FILTERS,
        seasons: ['1', '2'],
        langs: ['en'],
      }).map((i) => i.title),
    ).toEqual(['Show S02 1080p'])

    expect(
      applyV2ClientFilters(items, {
        ...EMPTY_V2_FILTERS,
        videotype: 'hdr',
      }).map((i) => i.title),
    ).toEqual(['Show S01 2160p HDR'])
  })
})
