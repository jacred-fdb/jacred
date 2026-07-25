/**
 * Lightweight CSS stagger for card grids (stats).
 * @param container Results list root
 * @param fromIndex Animate only cards from this index (load-more)
 */
export function animateResultCards(
  container: HTMLElement | null | undefined,
  fromIndex = 0,
) {
  if (!container || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    return
  }
  const cards = Array.from(
    container.querySelectorAll<HTMLElement>('[data-result-card]'),
  )
  const targets = fromIndex > 0 ? cards.slice(fromIndex) : cards
  if (!targets.length) return

  targets.forEach((card, index) => {
    card.classList.remove('jr-card-enter')
    card.style.animationDelay = `${Math.min(index, 12) * 20}ms`
    void card.offsetWidth
    card.classList.add('jr-card-enter')
    card.addEventListener(
      'animationend',
      () => {
        card.classList.remove('jr-card-enter')
        card.style.removeProperty('animation-delay')
      },
      { once: true },
    )
  })
}
