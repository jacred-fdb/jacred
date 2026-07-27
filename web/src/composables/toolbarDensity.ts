export type ToolbarDensity = 'compact' | 'regular' | 'comfortable'

export const DENSITY_ORDER: ToolbarDensity[] = [
  'compact',
  'regular',
  'comfortable',
]

/** Spare width (px) required before stepping density up after a successful fit. */
export const TOOLBAR_DENSITY_SPARE_PX = 48

export function densityRank(d: ToolbarDensity) {
  return DENSITY_ORDER.indexOf(d)
}

/**
 * Step toolbar density when Band 2 overflows or has spare room.
 * Pure helper — unit-tested; used by ResizeObserver.
 */
export function nextDensity(
  overflow: boolean,
  current: ToolbarDensity,
  sparePx = 0,
): ToolbarDensity {
  const i = densityRank(current)
  const idx = i < 0 ? 1 : i
  if (overflow && idx > 0) return DENSITY_ORDER[idx - 1]!
  if (!overflow && sparePx >= TOOLBAR_DENSITY_SPARE_PX && idx < DENSITY_ORDER.length - 1) {
    return DENSITY_ORDER[idx + 1]!
  }
  return DENSITY_ORDER[idx]!
}

export function densityFromDockWidth(widthPx: number): ToolbarDensity {
  // Match CSS @container rem steps (1rem ≈ 16px): 40rem / 56rem
  if (widthPx >= 56 * 16) return 'comfortable'
  if (widthPx >= 40 * 16) return 'regular'
  return 'compact'
}
