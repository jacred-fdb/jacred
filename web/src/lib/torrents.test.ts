import { describe, expect, it } from 'vitest'
import {
  applyClientFilters,
  buildFacets,
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
    voices: ['EN'],
    types: ['movie'],
    quality: 1080,
  },
  {
    tracker: 'two',
    title: 'Movie Dubbed',
    sid: 8,
    size: 5,
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
