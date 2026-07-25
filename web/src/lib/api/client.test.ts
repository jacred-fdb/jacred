import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/lib/api/client'

vi.mock('@/lib/storage', () => ({
  getApiKey: () => 'api-secret',
  getDevKey: () => 'dev-secret',
}))

afterEach(() => {
  vi.restoreAllMocks()
})

describe('apiRequest', () => {
  it('adds configured auth headers', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(
        new Response(JSON.stringify({ ok: true }), {
          headers: { 'Content-Type': 'application/json' },
        }),
      )

    await apiRequest('/test', {}, { withDevKey: true })

    const headers = new Headers(fetchMock.mock.calls[0]?.[1]?.headers)
    expect(headers.get('X-Api-Key')).toBe('api-secret')
    expect(headers.get('X-Dev-Key')).toBe('dev-secret')
  })

  it('throws a typed error for non-success responses', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ message: 'denied' }), {
        status: 403,
        headers: { 'Content-Type': 'application/json' },
      }),
    )

    await expect(apiRequest('/test')).rejects.toMatchObject({
      name: 'ApiError',
      status: 403,
      message: 'denied',
    })
  })

  it('honors caller cancellation', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(
      (_input, init) =>
        new Promise((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () =>
            reject(new DOMException('Aborted', 'AbortError')),
          )
        }),
    )
    const controller = new AbortController()
    const request = apiRequest('/test', {}, { signal: controller.signal })
    controller.abort()
    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
  })
})
