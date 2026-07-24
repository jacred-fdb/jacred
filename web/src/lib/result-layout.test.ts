import { describe, expect, it } from 'vitest'
import {
  RESULT_ESTIMATE,
  resultEstimateSize,
  resultGap,
  RESULT_CARD_GAP,
  RESULT_LIST_GAP_DESKTOP,
  RESULT_LIST_GAP_MOBILE,
} from '@/lib/result-layout'

describe('result-layout', () => {
  it('returns list/card estimates for breakpoints', () => {
    expect(resultEstimateSize(true, true)).toBe(RESULT_ESTIMATE.list.sm)
    expect(resultEstimateSize(true, false)).toBe(RESULT_ESTIMATE.list.mobile)
    expect(resultEstimateSize(false, true)).toBe(RESULT_ESTIMATE.card.sm)
    expect(resultEstimateSize(false, false)).toBe(RESULT_ESTIMATE.card.mobile)
  })

  it('returns gaps matching list/card density', () => {
    expect(resultGap(true, true)).toBe(RESULT_LIST_GAP_DESKTOP)
    expect(resultGap(true, false)).toBe(RESULT_LIST_GAP_MOBILE)
    expect(resultGap(false, true)).toBe(RESULT_CARD_GAP)
    expect(resultGap(false, false)).toBe(RESULT_CARD_GAP)
  })
})
