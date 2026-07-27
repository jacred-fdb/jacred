import { injectDialogRootContext } from 'reka-ui'
import {
  nextTick,
  onBeforeUnmount,
  watch,
  type Ref,
} from 'vue'

type AnimateFn = typeof import('motion').animate

let animateFn: AnimateFn | null = null

async function loadAnimate(): Promise<AnimateFn> {
  if (animateFn) return animateFn
  const { animate } = await import('motion')
  animateFn = animate
  return animate
}

function rubberband(overshoot: number, dimension: number, constant = 0.55) {
  return (
    (overshoot * dimension * constant) /
    (dimension + constant * Math.abs(overshoot))
  )
}

/** Exponential-decay fling projection (px). */
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
const SHEET_BODY_SEL = '.jr-sheet-body'
const SHEET_PANEL_SEL = '[data-slot="sheet-content"][data-side="bottom"]'
/** Commit to drag only after this much pointer travel (tap vs dismiss). */
const DRAG_HYSTERESIS_PX = 10
/** Flick speed that earns snap-back bounce (§4 momentum-only). */
const FLICK_BOUNCE_V = 400

/**
 * Bottom-sheet drag: 1:1 tracking, velocity handoff, spring open / settle / dismiss.
 *
 * IMPORTANT: Reka DialogContentImpl *replaces* `rootContext.contentElement` with a
 * new ref on mount. Watching the original empty ref never sees the panel — so we
 * resolve the DOM node via querySelector / event target, not a one-shot watch on
 * the stale context ref. (X button worked because it reads the live ref at click.)
 */
export function useSheetDrag(enabled: Ref<boolean>) {
  const dialog = injectDialogRootContext()

  let dragging = false
  let pending = false
  let pendingFromBody = false
  let startPointerY = 0
  let startPointerX = 0
  let originY = 0
  let currentY = 0
  let velocityY = 0
  let samples: VelocitySample[] = []
  let controls: { stop: () => void } | null = null
  let boundPanel: HTMLElement | null = null
  let activePointerId: number | null = null
  let openedForEl: HTMLElement | null = null
  let dismissFallbackTimer: number | null = null
  let windowBound = false

  function resolvePanel(): HTMLElement | null {
    const fromCtx = dialog.contentElement?.value
    if (fromCtx instanceof HTMLElement) return fromCtx
    return document.querySelector(SHEET_PANEL_SEL)
  }

  function panel(): HTMLElement | null {
    return boundPanel ?? resolvePanel()
  }

  function stopAnim() {
    controls?.stop()
    controls = null
  }

  function clearDismissFallback() {
    if (dismissFallbackTimer != null) {
      window.clearTimeout(dismissFallbackTimer)
      dismissFallbackTimer = null
    }
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

  function beginPending(el: HTMLElement, clientX: number, clientY: number, timeStamp: number) {
    stopAnim()
    el.style.transition = 'none'
    el.style.willChange = 'transform'
    originY = readLiveY(el)
    currentY = originY
    startPointerX = clientX
    startPointerY = clientY
    samples = [{ y: clientY, t: timeStamp }]
    velocityY = 0
    pending = true
    dragging = false
  }

  function abortPending(el: HTMLElement) {
    pending = false
    pendingFromBody = false
    dragging = false
    activePointerId = null
    clearMotionStyles(el)
    unbindWindow()
  }

  function commitDrag(el: HTMLElement) {
    pending = false
    dragging = true
    el.classList.add('jr-sheet--dragging')
    bindWindow()
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

  function sheetBodyAtTop(target: EventTarget | null): HTMLElement | null {
    if (!(target instanceof Element)) return null
    const body = target.closest(SHEET_BODY_SEL)
    if (!(body instanceof HTMLElement)) return null
    if (body.scrollTop > 1) return null
    return body
  }

  function springOpen(el: HTMLElement) {
    if (openedForEl === el) return
    openedForEl = el
    if (prefersReducedMotion()) {
      clearMotionStyles(el)
      return
    }
    stopAnim()
    const height = Math.max(el.offsetHeight, 320)
    setY(el, height)
    el.style.willChange = 'transform'
    el.style.transition = 'none'
    void loadAnimate().then((animate) => {
      if (!dialog.open.value || panel() !== el) {
        clearMotionStyles(el)
        return
      }
      const from = readLiveY(el) || height
      controls = animate(
        el,
        { y: [from, 0] },
        {
          type: 'spring',
          bounce: 0,
          duration: 0.3,
          onComplete: () => clearMotionStyles(el),
        },
      )
    })
  }

  function dismissWithSpring() {
    const el = panel()
    if (!el || prefersReducedMotion()) {
      dialog.onOpenChange(false)
      return
    }
    stopAnim()
    clearDismissFallback()
    const height = el.offsetHeight || 1
    const from = Math.max(0, readLiveY(el))
    currentY = from
    velocityY = 0
    el.classList.add('jr-sheet--drag-dismiss')
    el.style.transition = 'none'
    el.style.willChange = 'transform'
    const target = height + 48
    void loadAnimate().then((animate) => {
      if (!dialog.open.value) {
        clearMotionStyles(el)
        return
      }
      controls = animate(
        el,
        { y: [from, target] },
        {
          type: 'spring',
          bounce: 0,
          duration: 0.3,
          onComplete: () => {
            clearDismissFallback()
            dialog.onOpenChange(false)
            clearMotionStyles(el)
            openedForEl = null
          },
        },
      )
    })
    dismissFallbackTimer = window.setTimeout(() => {
      if (dialog.open.value) {
        dialog.onOpenChange(false)
        clearMotionStyles(el)
        openedForEl = null
      }
    }, 450)
  }

  function onPointerDown(e: PointerEvent) {
    if (!enabled.value || !dialog.open.value) return
    if (e.button !== 0) return

    const el = panel()
    if (!el) return
    // Ignore presses outside this sheet (document capture).
    if (e.target instanceof Node && !el.contains(e.target)) return

    const inChrome = isInDragZone(e.target)
    if (inChrome) {
      if (
        e.target instanceof Element &&
        e.target.closest('[data-slot="sheet-close"]')
      ) {
        return
      }
      pendingFromBody = false
      activePointerId = e.pointerId
      beginPending(el, e.clientX, e.clientY, e.timeStamp)
      bindWindow()
      try {
        el.setPointerCapture(e.pointerId)
      } catch {
        /* iOS may reject capture */
      }
      // Chrome zone: take the gesture so Safari doesn't scroll the page behind.
      e.preventDefault()
      return
    }

    if (
      e.target instanceof Element &&
      e.target.closest('[data-slot="sheet-close"]')
    ) {
      return
    }
    if (!sheetBodyAtTop(e.target)) return

    pendingFromBody = true
    activePointerId = e.pointerId
    beginPending(el, e.clientX, e.clientY, e.timeStamp)
    bindWindow()
    // No preventDefault yet — allow abort into native scroll.
  }

  function trackMove(
    el: HTMLElement,
    clientX: number,
    clientY: number,
    timeStamp: number,
  ): boolean {
    const reduced = prefersReducedMotion()

    if (pending && !dragging) {
      const dy = clientY - startPointerY
      const dx = clientX - startPointerX

      if (pendingFromBody) {
        if (
          dy < -DRAG_HYSTERESIS_PX ||
          (Math.abs(dx) > DRAG_HYSTERESIS_PX && Math.abs(dx) > Math.abs(dy))
        ) {
          abortPending(el)
          return false
        }
        if (dy < DRAG_HYSTERESIS_PX) {
          sampleVelocity(clientY, timeStamp)
          return false
        }
      } else if (Math.abs(dy) < DRAG_HYSTERESIS_PX && Math.abs(dx) < DRAG_HYSTERESIS_PX) {
        sampleVelocity(clientY, timeStamp)
        return false
      } else if (dy < DRAG_HYSTERESIS_PX) {
        // Chrome: only commit on downward pull (dismiss direction).
        if (dy < -DRAG_HYSTERESIS_PX) {
          abortPending(el)
          return false
        }
        sampleVelocity(clientY, timeStamp)
        return false
      }

      if (reduced) {
        pending = false
        pendingFromBody = false
        dragging = false
        activePointerId = null
        clearMotionStyles(el)
        unbindWindow()
        dialog.onOpenChange(false)
        openedForEl = null
        return false
      }

      originY = readLiveY(el)
      startPointerY = clientY
      startPointerX = clientX
      commitDrag(el)
      try {
        if (activePointerId != null) el.setPointerCapture(activePointerId)
      } catch {
        /* ignore */
      }
    }
    if (!dragging) return false

    const height = el.offsetHeight || 1
    let y = originY + (clientY - startPointerY)
    if (y < 0) y = -rubberband(-y, height)
    else if (y > height) y = height + rubberband(y - height, height)

    sampleVelocity(clientY, timeStamp)
    currentY = y
    setY(el, y)
    return true
  }

  function onPointerMove(e: PointerEvent) {
    if (!pending && !dragging) return
    if (activePointerId != null && e.pointerId !== activePointerId) return
    const el = panel()
    if (!el) return
    const tracking = trackMove(el, e.clientX, e.clientY, e.timeStamp)
    if (tracking || dragging) e.preventDefault()
  }

  function snapBack(el: HTMLElement) {
    const from = currentY
    const vel = velocityY
    const hadFlick = Math.abs(vel) > FLICK_BOUNCE_V
    void loadAnimate().then((animate) => {
      if (!dialog.open.value) {
        clearMotionStyles(el)
        return
      }
      controls = animate(
        el,
        { y: [from, 0] },
        {
          type: 'spring',
          bounce: hadFlick ? 0.12 : 0,
          duration: 0.3,
          velocity: vel,
          onComplete: () => clearMotionStyles(el),
        },
      )
    })
  }

  function finishDismiss(el: HTMLElement) {
    const height = el.offsetHeight || 1
    const from = Math.max(0, currentY)
    const projected = from + project(velocityY)
    const shouldDismiss =
      velocityY > 420 ||
      projected > height * 0.2 ||
      from > height * 0.2

    const hadFlick = Math.abs(velocityY) > FLICK_BOUNCE_V
    const vel = velocityY

    if (shouldDismiss) {
      clearDismissFallback()
      el.classList.add('jr-sheet--drag-dismiss')
      const target = height + 48
      void loadAnimate().then((animate) => {
        if (!dialog.open.value) {
          clearMotionStyles(el)
          return
        }
        controls = animate(
          el,
          { y: [from, target] },
          {
            type: 'spring',
            bounce: hadFlick ? 0.08 : 0,
            duration: 0.3,
            velocity: Math.max(0, vel),
            onComplete: () => {
              clearDismissFallback()
              dialog.onOpenChange(false)
              clearMotionStyles(el)
              openedForEl = null
            },
          },
        )
      })
      dismissFallbackTimer = window.setTimeout(() => {
        if (dialog.open.value) {
          dialog.onOpenChange(false)
          clearMotionStyles(el)
          openedForEl = null
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
    if (!pending && !dragging) return
    const wasDragging = dragging
    pending = false
    pendingFromBody = false
    dragging = false
    const pointerId = activePointerId
    activePointerId = null
    const el = panel()
    unbindWindow()
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

    if (
      !wasDragging ||
      (mode === 'cancel' &&
        Math.abs(currentY) < 10 &&
        Math.abs(velocityY) < 120)
    ) {
      if (wasDragging || Math.abs(currentY) > 0.5) snapBack(el)
      else clearMotionStyles(el)
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
    if (!pending && !dragging) return
    if (activePointerId != null && e.pointerId !== activePointerId) return
    endDrag(e.clientY, e.timeStamp, 'up')
  }

  function onTouchEnd(e: TouchEvent) {
    if (!pending && !dragging) return
    const touch = e.changedTouches[0]
    endDrag(touch?.clientY, e.timeStamp, 'up')
  }

  function onTouchMove(e: TouchEvent) {
    if (!pending && !dragging) return
    const touch = e.touches[0]
    if (!touch) return
    const el = panel()
    if (!el) return
    const tracking = trackMove(el, touch.clientX, touch.clientY, e.timeStamp)
    if (tracking || dragging) e.preventDefault()
  }

  function unbindWindow() {
    if (!windowBound) return
    windowBound = false
    window.removeEventListener('pointermove', onPointerMove)
    window.removeEventListener('pointerup', onPointerUp)
    window.removeEventListener('pointercancel', onPointerCancel)
    window.removeEventListener('touchmove', onTouchMove)
    window.removeEventListener('touchend', onTouchEnd)
    window.removeEventListener('touchcancel', onTouchEnd)
  }

  function bindWindow() {
    if (windowBound) return
    windowBound = true
    // Window-level tracking survives iOS pointercancel on the panel.
    window.addEventListener('pointermove', onPointerMove, { passive: false })
    window.addEventListener('pointerup', onPointerUp, { passive: false })
    window.addEventListener('pointercancel', onPointerCancel, { passive: false })
    window.addEventListener('touchmove', onTouchMove, { passive: false })
    window.addEventListener('touchend', onTouchEnd, { passive: false })
    window.addEventListener('touchcancel', onTouchEnd, { passive: false })
  }

  function unbindPanel() {
    unbindWindow()
    if (!boundPanel) return
    boundPanel.removeEventListener('pointerdown', onPointerDown)
    boundPanel.removeEventListener('lostpointercapture', onLostPointerCapture)
    boundPanel = null
  }

  function bindPanel(el: HTMLElement | null) {
    unbindPanel()
    if (!el || !enabled.value) return
    boundPanel = el
    el.addEventListener('pointerdown', onPointerDown, { passive: false })
    el.addEventListener('lostpointercapture', onLostPointerCapture, {
      passive: true,
    })
  }

  async function attachWhenOpen() {
    unbindPanel()
    openedForEl = null
    clearDismissFallback()
    if (!enabled.value || !dialog.open.value) return

    // Reka replaces contentElement on Content mount — wait for DOM.
    await nextTick()
    await nextTick()
    let el = resolvePanel()
    if (!el) {
      await new Promise<void>((r) => requestAnimationFrame(() => r()))
      el = resolvePanel()
    }
    if (!el || !dialog.open.value) return
    bindPanel(el)
    springOpen(el)
  }

  watch(
    [enabled, () => dialog.open.value],
    () => {
      void attachWhenOpen()
    },
    { immediate: true, flush: 'post' },
  )

  onBeforeUnmount(() => {
    stopAnim()
    clearDismissFallback()
    unbindPanel()
  })

  return { dismissWithSpring }
}
