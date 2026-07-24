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
 * (handle and/or header) — required for reliable dismiss on iOS Safari / Edge.
 *
 * iOS often fires `pointercancel` instead of `pointerup` on finger lift; treat
 * cancel-with-drag as a release, not an abort.
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
      /* iOS may reject capture — still track via bubbling listeners */
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

  function snapBack(el: HTMLElement) {
    const from = currentY
    controls = animate(
      el,
      { y: [from, 0] },
      {
        type: 'spring',
        bounce: 0.12,
        duration: 0.4,
        velocity: velocityY,
        onComplete: () => clearMotionStyles(el),
      },
    )
  }

  function finishDismiss(el: HTMLElement) {
    const height = el.offsetHeight || 1
    const from = Math.max(0, currentY)
    const projected = from + project(velocityY)
    // Apple-like: short flick or ~20% pull dismisses (was 32% / 650 — too stiff on iOS).
    const shouldDismiss =
      velocityY > 420 ||
      projected > height * 0.2 ||
      from > height * 0.2

    const hadFlick = Math.abs(velocityY) > 400

    if (shouldDismiss) {
      el.classList.add('jr-sheet--drag-dismiss')
      const target = height + 48
      controls = animate(
        el,
        { y: [from, target] },
        {
          type: 'spring',
          bounce: hadFlick ? 0.08 : 0,
          duration: 0.32,
          velocity: Math.max(0, velocityY),
          onComplete: () => {
            dialog.onOpenChange(false)
            clearMotionStyles(el)
          },
        },
      )
      // Fallback if the spring never completes (WebKit / Motion edge cases)
      window.setTimeout(() => {
        if (dialog.open.value) {
          dialog.onOpenChange(false)
          clearMotionStyles(el)
        }
      }, 450)
    } else {
      snapBack(el)
    }
  }

  function endDrag(
    clientY: number | undefined,
    timeStamp: number | undefined,
    mode: 'up' | 'cancel',
  ) {
    if (!dragging) return
    dragging = false
    const pointerId = activePointerId
    activePointerId = null
    const el = panel()
    if (!el) return

    if (clientY != null && timeStamp != null) {
      sampleVelocity(clientY, timeStamp)
    }

    try {
      if (pointerId != null && el.hasPointerCapture?.(pointerId)) {
        el.releasePointerCapture(pointerId)
      }
    } catch {
      /* ignore */
    }

    // Bare cancel with almost no movement → snap back.
    // iOS often sends pointercancel on finger-up after a real drag → treat as release.
    if (
      mode === 'cancel' &&
      Math.abs(currentY) < 10 &&
      Math.abs(velocityY) < 120
    ) {
      snapBack(el)
      return
    }

    finishDismiss(el)
  }

  function onPointerUp(e: PointerEvent) {
    if (activePointerId != null && e.pointerId !== activePointerId) return
    endDrag(e.clientY, e.timeStamp, 'up')
  }

  function onPointerCancel(e: PointerEvent) {
    if (activePointerId != null && e.pointerId !== activePointerId) return
    endDrag(e.clientY, e.timeStamp, 'cancel')
  }

  function onLostPointerCapture(e: PointerEvent) {
    if (!dragging) return
    if (activePointerId != null && e.pointerId !== activePointerId) return
    endDrag(e.clientY, e.timeStamp, 'up')
  }

  /** Touch fallback when Pointer Events are incomplete (older WebKit). */
  function onTouchEnd(e: TouchEvent) {
    if (!dragging) return
    const touch = e.changedTouches[0]
    endDrag(touch?.clientY, e.timeStamp, 'up')
  }

  function onTouchMove(e: TouchEvent) {
    if (!dragging) return
    e.preventDefault()
    const touch = e.touches[0]
    if (!touch) return
    const el = panel()
    if (!el) return

    const height = el.offsetHeight || 1
    let y = originY + (touch.clientY - startPointerY)
    if (y < 0) y = -rubberband(-y, height)
    else if (y > height) y = height + rubberband(y - height, height)

    sampleVelocity(touch.clientY, e.timeStamp)
    currentY = y
    setY(el, y)
  }

  function unbindPanel() {
    if (!boundPanel) return
    boundPanel.removeEventListener('pointerdown', onPointerDown)
    boundPanel.removeEventListener('pointermove', onPointerMove)
    boundPanel.removeEventListener('pointerup', onPointerUp)
    boundPanel.removeEventListener('pointercancel', onPointerCancel)
    boundPanel.removeEventListener('lostpointercapture', onLostPointerCapture)
    boundPanel.removeEventListener('touchmove', onTouchMove)
    boundPanel.removeEventListener('touchend', onTouchEnd)
    boundPanel.removeEventListener('touchcancel', onTouchEnd)
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
    el.addEventListener('lostpointercapture', onLostPointerCapture, {
      passive: true,
    })
    el.addEventListener('touchmove', onTouchMove, { passive: false })
    el.addEventListener('touchend', onTouchEnd, { passive: false })
    el.addEventListener('touchcancel', onTouchEnd, { passive: false })
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
