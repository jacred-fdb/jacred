/** Shared segmented-control class strings (Search / Stats / Settings). */

export const segmentTrack =
  'jr-segment-track flex h-8 w-max max-w-full flex-nowrap items-center rounded-[10px] bg-secondary p-0.5 shadow-none ring-0'

/** Settings category tabs — wraps instead of horizontal scroll. */
export const segmentTrackWrap =
  'jr-segment-track flex h-auto w-full max-w-full flex-wrap items-center justify-start gap-0.5 rounded-[10px] bg-secondary p-0.5 shadow-none ring-0'

/** Sort strip — equal chips on phone, intrinsic width on lg. */
export const segmentTrackSort =
  'jr-segment-track flex h-8 w-full max-w-full flex-nowrap items-center rounded-[10px] bg-secondary p-0.5 shadow-none ring-0 lg:w-max lg:max-w-none'

export const segmentTrackCompact =
  'jr-segment-track flex h-7 w-max flex-nowrap items-center rounded-[10px] bg-secondary p-0.5 shadow-none ring-0'

export const segmentTrackChrome =
  'jr-segment-track flex h-9 w-max flex-nowrap items-center rounded-[10px] bg-secondary p-0.5 shadow-none ring-0'

const segmentOn =
  'data-[state=on]:!bg-background data-[state=on]:!text-foreground data-[state=on]:shadow-[0_1px_2px_rgba(0,0,0,0.28)] data-active:bg-background data-active:text-foreground data-active:shadow-[0_1px_2px_rgba(0,0,0,0.28)]'

export const segmentItem =
  `!rounded-[8px] h-full gap-1.5 border-0 bg-transparent px-2.5 text-xs font-medium text-muted-foreground shadow-none outline-none ring-0 hover:!bg-transparent hover:text-foreground focus-visible:!border-transparent focus-visible:!ring-2 focus-visible:!ring-ring/40 sm:text-[13px] ${segmentOn}`

export const segmentItemSort =
  `${segmentItem} min-w-0 flex-1 justify-center gap-1 !px-1.5 text-[11px] leading-tight whitespace-nowrap lg:flex-none lg:shrink-0 lg:gap-1.5 lg:!px-2.5 lg:text-[13px]`

export const segmentItemWrap =
  `!rounded-[8px] h-8 shrink-0 gap-1 border-0 bg-transparent px-2 text-xs font-medium text-muted-foreground shadow-none outline-none ring-0 hover:!bg-transparent hover:text-foreground sm:gap-1.5 sm:px-2.5 ${segmentOn}`

export const segmentItemCompact =
  `!rounded-[8px] h-full gap-1 border-0 bg-transparent px-2 text-xs font-medium text-muted-foreground shadow-none ${segmentOn}`
