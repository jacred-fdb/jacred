import { animate } from 'motion'
import { injectDialogRootContext } from 'reka-ui'
import {
  onBeforeUnmount,
  watch,
  type Ref,
} from 'vue'

function rubberband(overshoot: number, dimension: number, constant = 0.55) {
  return (
    (overshoot * dimension * constant) /
    (dimension + constant * Math.abs(overshoot))
  )
}

/** Apple exponential-decay projection (px). */
function project(initialVelocity: number, decelerationRate = 0.998) {
  return ((initialVelocity / 1000) * decelerationRate) / (1 - decelerationRate)
}

function prefersReducedMotion() {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

function readLiveY(el: HTMLElement): number {
  const t = getComputedStyle(el).transform
  if (!t || t === 'none') return 0
  try {
    return new DOMMatrixReadOnly(t).m42
  } catch {
    return 0
  }
}

type VelocitySample = { y: number; t: number }

const DRAG_ZONE_SEL = '.jr-sheet-drag-zone'

/**
 * Bottom-sheet drag: 1:1 tracking, velocity handoff, spring settle / dismiss.
 * Listens on the sheet panel (non-passive) and starts only from `.jr-sheet-drag-zone`
 * (handle and/or header) — required for reliable dismiss on iOS Safari.
 */
export function useSheetDrag(enabled: Ref<boolean>) {
  const dialog = injectDialogRootContext()

  let dragging = false
  let startPointerY = 0
  let originY = 0
  let currentY = 0
  let velocityY = 0
  let samples: VelocitySample[] = []
  let controls: { stop: () => void } | null = null
  let boundPanel: HTMLElement | null = null
  let activePointerId: number | null = null

  function panel(): HTMLElement | null {
    return dialog.contentElement.value ?? null
  }

  function stopAnim() {
    controls?.stop()
    controls = null
  }

  function setY(el: HTMLElement, y: number) {
    el.style.transform = `translate3d(0, ${y}px, 0)`
  }

  function clearMotionStyles(el: HTMLElement) {
    el.style.removeProperty('transform')
    el.style.removeProperty('transition')
    el.style.removeProperty('will-change')
    el.classList.remove('jr-sheet--drag-dismiss', 'jr-sheet--dragging')
  }

  function beginDrag(el: HTMLElement, clientY: number, timeStamp: number) {
    stopAnim()
    el.classList.add('jr-sheet--dragging')
    el.style.transition = 'none'
    el.style.willChange = 'transform'
    originY = readLiveY(el)
    currentY = originY
    startPointerY = clientY
    samples = [{ y: clientY, t: timeStamp }]
    velocityY = 0
    dragging = true
  }

  function sampleVelocity(clientY: number, timeStamp: number) {
    samples.push({ y: clientY, t: timeStamp })
    while (samples.length > 5) samples.shift()
    if (samples.length < 2) {
      velocityY = 0
      return
    }
    const first = samples[0]!
    const last = samples[samples.length - 1]!
    const dt = Math.max(1, last.t - first.t)
    velocityY = ((last.y - first.y) / dt) * 1000
  }

  function isInDragZone(target: EventTarget | null): boolean {
    return target instanceof Element && !!target.closest(DRAG_ZONE_SEL)
  }

  function onPointerDown(e: PointerEvent) {
    if (!enabled.value || prefersReducedMotion()) return
    if (e.button !== 0) return
    if (!isInDragZone(e.target)) return
    // Don't steal clicks from the close button inside the chrome
    if (
      e.target instanceof Element &&
      e.target.closest('[data-slot="sheet-close"]')
    ) {
      return
    }

    const el = panel()
    if (!el) return

    activePointerId = e.pointerId
    beginDrag(el, e.clientY, e.timeStamp)
    try {
      el.setPointerCapture(e.pointerId)
    } catch {
      /* ignore */
    }
    e.preventDefault()
  }

  function onPointerMove(e: PointerEvent) {
    if (!dragging) return
    if (activePointerId != null && e.pointerId !== activePointerId) return
    const el = panel()
    if (!el) return

    const height = el.offsetHeight || 1
    let y = originY + (e.clientY - startPointerY)
    if (y < 0) y = -rubberband(-y, height)
    else if (y > height) y = height + rubberband(y - height, height)

    sampleVelocity(e.clientY, e.timeStamp)
    currentY = y
    setY(el, y)
    e.preventDefault()
  }

  function finishDismiss(el: HTMLElement) {
    const height = el.offsetHeight || 1
    const projected = currentY + project(velocityY)
    const shouldDismiss =
      velocityY > 650 ||
      projected > height * 0.32 ||
      currentY > height * 0.32

    const hadFlick = Math.abs(velocityY) > 550

    if (shouldDismiss) {
      el.classList.add('jr-sheet--drag-dismiss')
      controls = animate(
        el,
        { y: height + 48 },
        {
          type: 'spring',
          bounce: hadFlick ? 0.12 : 0,
          duration: 0.35,
          velocity: velocityY,
          onComplete: () => {
            dialog.onOpenChange(false)
            clearMotionStyles(el)
          },
        },
      )
    } else {
      controls = animate(
        el,
        { y: 0 },
        {
          type: 'spring',
          bounce: hadFlick ? 0.18 : 0,
          duration: 0.4,
          velocity: velocityY,
          onComplete: () => {
            clearMotionStyles(el)
          },
        },
      )
    }
  }

  function onPointerUp(e: PointerEvent) {
    if (!dragging) return
    if (activePointerId != null && e.pointerId !== activePointerId) return
    dragging = false
    activePointerId = null
    const el = panel()
    if (!el) return
    sampleVelocity(e.clientY, e.timeStamp)
    finishDismiss(el)
  }

  function onPointerCancel(e: PointerEvent) {
    if (!dragging) return
    if (activePointerId != null && e.pointerId !== activePointerId) return
    dragging = false
    activePointerId = null
    const el = panel()
    if (!el) return
    controls = animate(
      el,
      { y: 0 },
      {
        type: 'spring',
        bounce: 0,
        duration: 0.35,
        onComplete: () => clearMotionStyles(el),
      },
    )
  }

  function onTouchMove(e: TouchEvent) {
    if (!dragging) return
    e.preventDefault()
  }

  function unbindPanel() {
    if (!boundPanel) return
    boundPanel.removeEventListener('pointerdown', onPointerDown)
    boundPanel.removeEventListener('pointermove', onPointerMove)
    boundPanel.removeEventListener('pointerup', onPointerUp)
    boundPanel.removeEventListener('pointercancel', onPointerCancel)
    boundPanel.removeEventListener('touchmove', onTouchMove)
    boundPanel = null
  }

  function bindPanel(el: HTMLElement | null) {
    unbindPanel()
    if (!el || !enabled.value) return
    boundPanel = el
    el.addEventListener('pointerdown', onPointerDown, { passive: false })
    el.addEventListener('pointermove', onPointerMove, { passive: false })
    el.addEventListener('pointerup', onPointerUp, { passive: false })
    el.addEventListener('pointercancel', onPointerCancel, { passive: false })
    el.addEventListener('touchmove', onTouchMove, { passive: false })
  }

  watch(
    [enabled, () => dialog.contentElement.value],
    () => {
      bindPanel(enabled.value ? panel() : null)
    },
    { immediate: true, flush: 'post' },
  )

  onBeforeUnmount(() => {
    stopAnim()
    unbindPanel()
  })
}
