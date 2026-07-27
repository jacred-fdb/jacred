import { describe, expect, it } from 'vitest'
import {
  resultGap,
  RESULT_CARD_GAP,
  RESULT_LIST_GAP_DESKTOP,
  RESULT_LIST_GAP_MOBILE,
} from '@/lib/result-layout'

describe('result-layout', () => {
  it('returns gaps matching list/card density', () => {
    expect(resultGap(true, true)).toBe(RESULT_LIST_GAP_DESKTOP)
    expect(resultGap(true, false)).toBe(RESULT_LIST_GAP_MOBILE)
    expect(resultGap(false, true)).toBe(RESULT_CARD_GAP)
    expect(resultGap(false, false)).toBe(RESULT_CARD_GAP)
  })
})
