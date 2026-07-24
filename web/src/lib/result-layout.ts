/** Shared row estimates for VirtualList + skeletons (must stay in sync). */

export const RESULT_LIST_GAP_MOBILE = 8
export const RESULT_LIST_GAP_DESKTOP = 4
export const RESULT_CARD_GAP = 10

/** Estimated row heights (px) — tune with TorrentCard CSS density. */
export const RESULT_ESTIMATE = {
  list: { sm: 72, mobile: 148 },
  card: { sm: 124, mobile: 156 },
} as const

export function resultEstimateSize(
  listView: boolean,
  isSmUp: boolean,
): number {
  if (listView) {
    return isSmUp ? RESULT_ESTIMATE.list.sm : RESULT_ESTIMATE.list.mobile
  }
  return isSmUp ? RESULT_ESTIMATE.card.sm : RESULT_ESTIMATE.card.mobile
}

export function resultGap(listView: boolean, isSmUp: boolean): number {
  if (listView) {
    return isSmUp ? RESULT_LIST_GAP_DESKTOP : RESULT_LIST_GAP_MOBILE
  }
  return RESULT_CARD_GAP
}

/** Custom event: FAB / shell asks the active VirtualList to remasure. */
export const JR_VIRTUAL_REMEASURE = 'jr:virtual-remeasure'
