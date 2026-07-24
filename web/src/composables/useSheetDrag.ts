import { animate } from 'motion'
import { injectDialogRootContext } from 'reka-ui'
import { onBeforeUnmount, type Ref } from 'vue'

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

/**
 * Bottom-sheet drag: 1:1 tracking, velocity handoff, spring settle / dismiss.
 * Wire pointer events on the drag handle only.
 */
export function useSheetDrag(enabled: Ref<boolean>) {
  const dialog = injectDialogRootContext()

  let dragging = false
  let startPointerY = 0
  let originY = 0
  let currentY = 0
  let lastY = 0
  let lastT = 0
  let velocityY = 0
  let controls: { stop: () => void } | null = null

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
    el.classList.remove('jr-sheet--drag-dismiss')
  }

  function onPointerDown(e: PointerEvent) {
    if (!enabled.value || prefersReducedMotion()) return
    if (e.button !== 0) return
    const el = panel()
    if (!el) return

    stopAnim()
    el.style.transition = 'none'
    originY = readLiveY(el)
    currentY = originY
    startPointerY = e.clientY
    lastY = e.clientY
    lastT = e.timeStamp
    velocityY = 0
    dragging = true
    ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
    e.preventDefault()
  }

  function onPointerMove(e: PointerEvent) {
    if (!dragging) return
    const el = panel()
    if (!el) return

    const height = el.offsetHeight || 1
    let y = originY + (e.clientY - startPointerY)
    if (y < 0) y = -rubberband(-y, height)
    else if (y > height) y = height + rubberband(y - height, height)

    const dt = Math.max(1, e.timeStamp - lastT)
    velocityY = ((e.clientY - lastY) / dt) * 1000
    lastY = e.clientY
    lastT = e.timeStamp
    currentY = y
    setY(el, y)
  }

  function onPointerUp() {
    if (!dragging) return
    dragging = false
    const el = panel()
    if (!el) return

    const height = el.offsetHeight || 1
    const projected = currentY + project(velocityY)
    const shouldDismiss =
      velocityY > 700 ||
      projected > height * 0.35 ||
      currentY > height * 0.35

    const hadFlick = Math.abs(velocityY) > 600

    if (shouldDismiss) {
      el.classList.add('jr-sheet--drag-dismiss')
      controls = animate(
        el,
        { y: height + 48 },
        {
          type: 'spring',
          bounce: hadFlick ? 0.15 : 0,
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
          bounce: hadFlick ? 0.2 : 0,
          duration: 0.4,
          velocity: velocityY,
          onComplete: () => {
            clearMotionStyles(el)
          },
        },
      )
    }
  }

  function onPointerCancel() {
    if (!dragging) return
    dragging = false
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

  onBeforeUnmount(() => {
    stopAnim()
  })

  return {
    onPointerDown,
    onPointerMove,
    onPointerUp,
    onPointerCancel,
  }
}
