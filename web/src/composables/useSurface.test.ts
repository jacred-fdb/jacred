import { beforeEach, describe, expect, it, vi } from 'vitest'

function fakeStorage() {
  const values = new Map<string, string>()
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => values.set(key, value),
    removeItem: (key: string) => values.delete(key),
  }
}

beforeEach(() => {
  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    value: fakeStorage(),
  })
  document.documentElement.removeAttribute('data-surface')
  vi.resetModules()
})

describe('useSurface', () => {
  it('writes data-surface on the document element', async () => {
    const { useSurface } = await import('@/composables/useSurface')
    const { setSurface, surface } = useSurface()

    expect(document.documentElement.dataset.surface).toBe('solid')
    expect(surface.value).toBe('solid')

    setSurface('glass')
    expect(surface.value).toBe('glass')
    expect(document.documentElement.dataset.surface).toBe('glass')
    expect(localStorage.getItem('jacredSurface')).toBe('glass')
  })
})
