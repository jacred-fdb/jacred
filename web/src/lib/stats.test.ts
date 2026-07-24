import { describe, expect, it } from 'vitest'
import {
  aggregateTrackers,
  filterAndSortTrackers,
  formatStatNumber,
  getTracksData,
  type TrackerStat,
} from '@/lib/stats'

const rows: TrackerStat[] = [
  {
    trackerName: 'rutor',
    newtor: 2,
    alltorrents: 10,
    tracks: { confirm: 3, wait: 1, skip: 0 },
  },
  {
    trackerName: 'kinozal',
    newtor: 5,
    alltorrents: 20,
    tracks: { confirm: 4, wait: 0, skip: 2 },
  },
]

describe('stats helpers', () => {
  it('normalizes missing track counters', () => {
    expect(getTracksData({ trackerName: 'x' })).toEqual({
      confirm: 0,
      wait: 0,
      skip: 0,
    })
  })

  it('filters, sorts and aggregates tracker rows', () => {
    expect(filterAndSortTrackers(rows, '', 'newtor')[0]?.trackerName).toBe(
      'kinozal',
    )
    expect(filterAndSortTrackers(rows, 'rutor', 'name')).toHaveLength(1)
    expect(aggregateTrackers(rows)).toMatchObject({
      newtor: 7,
      alltorrents: 30,
      confirm: 7,
      wait: 1,
      skip: 2,
    })
  })

  it('formats compact values', () => {
    expect(formatStatNumber(1_500, false, 'en')).toBe('1.5K')
  })
})
