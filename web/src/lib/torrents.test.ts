import { describe, expect, it } from 'vitest'
import {
  applyClientFilters,
  buildFacets,
  normalizeSortDirection,
  sortItems,
  splitTrackerNames,
  torrentKey,
  type TorrentItem,
} from '@/lib/torrents'

const items: TorrentItem[] = [
  {
    tracker: 'one',
    title: 'Movie Director Cut',
    sid: 3,
    size: 10,
    createTime: 100,
    voices: ['EN'],
    types: ['movie'],
    quality: 1080,
  },
  {
    tracker: 'two',
    title: 'Movie Dubbed',
    sid: 8,
    size: 5,
    createTime: 200,
    voices: ['RU'],
    types: ['movie'],
    quality: 2160,
  },
]

describe('torrent collection helpers', () => {
  it('sorts without mutating input', () => {
    const sorted = sortItems(items, 'sid')
    expect(sorted.map((item) => item.sid)).toEqual([8, 3])
    expect(items[0]?.sid).toBe(3)
  })

  it('sorts ascending when requested', () => {
    expect(sortItems(items, 'sid', 'asc').map((item) => item.sid)).toEqual([
      3, 8,
    ])
    expect(
      sortItems(items, 'date', 'asc').map((item) => item.createTime),
    ).toEqual([100, 200])
    expect(
      sortItems(items, 'date', 'desc').map((item) => item.createTime),
    ).toEqual([200, 100])
  })

  it('normalizes sort direction', () => {
    expect(normalizeSortDirection('asc')).toBe('asc')
    expect(normalizeSortDirection('ASC')).toBe('asc')
    expect(normalizeSortDirection('desc')).toBe('desc')
    expect(normalizeSortDirection('nope')).toBe('desc')
    expect(normalizeSortDirection(null)).toBe('desc')
  })

  it('filters by include and exclude title fragments', () => {
    expect(
      applyClientFilters(items, 'movie', 'dubbed'),
    ).toEqual([items[0]])
  })

  it('builds stable facets and identities', () => {
    expect(buildFacets(items).tracker).toEqual(['one', 'two'])
    expect(buildFacets(items).quality).toEqual(['1080', '2160'])
    expect(torrentKey(items[0]!)).toContain('one')
  })

  it('splits merged tracker names for facets', () => {
    expect(splitTrackerNames('mazepa, toloka')).toEqual(['mazepa', 'toloka'])
    expect(
      buildFacets([
        { tracker: 'mazepa, toloka', title: 'a' },
        { tracker: 'rutor', title: 'b' },
        { tracker: 'toloka', title: 'c' },
      ]).tracker,
    ).toEqual(['mazepa', 'rutor', 'toloka'])
    expect(
      buildFacets([{ tracker: 'a, b' }], { splitTrackers: false }).tracker,
    ).toEqual(['a, b'])
  })
})
