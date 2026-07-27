/** Shared result list gaps (SearchPage + skeletons). */

export const RESULT_LIST_GAP_MOBILE = 8
export const RESULT_LIST_GAP_DESKTOP = 4
/** Space between result cards. */
export const RESULT_CARD_GAP = 8

export function resultGap(listView: boolean, isSmUp: boolean): number {
  if (listView) {
    return isSmUp ? RESULT_LIST_GAP_DESKTOP : RESULT_LIST_GAP_MOBILE
  }
  return RESULT_CARD_GAP
}
