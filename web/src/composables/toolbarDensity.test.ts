import { describe, expect, it } from 'vitest'
import {
  densityFromDockWidth,
  nextDensity,
  TOOLBAR_DENSITY_SPARE_PX,
} from '@/composables/toolbarDensity'

describe('nextDensity', () => {
  it('steps down when overflowing', () => {
    expect(nextDensity(true, 'comfortable')).toBe('regular')
    expect(nextDensity(true, 'regular')).toBe('compact')
    expect(nextDensity(true, 'compact')).toBe('compact')
  })

  it('steps up only with enough spare room', () => {
    expect(nextDensity(false, 'compact', TOOLBAR_DENSITY_SPARE_PX - 1)).toBe(
      'compact',
    )
    expect(nextDensity(false, 'compact', TOOLBAR_DENSITY_SPARE_PX)).toBe(
      'regular',
    )
    expect(nextDensity(false, 'regular', TOOLBAR_DENSITY_SPARE_PX)).toBe(
      'comfortable',
    )
    expect(nextDensity(false, 'comfortable', 200)).toBe('comfortable')
  })
})

describe('densityFromDockWidth', () => {
  it('maps dock width to CQ-aligned density tiers', () => {
    expect(densityFromDockWidth(500)).toBe('compact')
    expect(densityFromDockWidth(40 * 16)).toBe('regular')
    expect(densityFromDockWidth(56 * 16)).toBe('comfortable')
  })
})
