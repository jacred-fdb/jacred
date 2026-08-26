import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiClient, apiRequest, buildUrl } from '@/lib/api/client'

vi.mock('@/lib/storage', () => ({
  getApiKey: () => 'api-secret',
  getDevKey: () => 'dev-secret',
}))

afterEach(() => {
  vi.restoreAllMocks()
})

describe('buildUrl', () => {
  it('encodes repeated array query params', () => {
    const url = buildUrl('https://example.test', '/api/v2.0/indexers/all/results', {
      query: 'matrix',
      'Category[]': ['2000', '5070'],
      year: 1999,
    })
    expect(url).toContain('query=matrix')
    expect(url).toContain('year=1999')
    expect(url).toContain('Category%5B%5D=2000')
    expect(url).toContain('Category%5B%5D=5070')
  })
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

describe('apiClient.getConf', () => {
  it('passes stored apikey as query and X-Api-Key header', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(
        new Response(JSON.stringify({
          jacred: true,
          configured: false,
          apikey: true,
          version: '3.7.1-next+726f1544',
        }), {
          headers: { 'Content-Type': 'application/json' },
        }),
      )

    await apiClient.getConf()

    const called = String(fetchMock.mock.calls[0]?.[0])
    expect(called).toContain('/api/v1.0/conf')
    expect(called).toContain('apikey=api-secret')
    const headers = new Headers(fetchMock.mock.calls[0]?.[1]?.headers)
    expect(headers.get('X-Api-Key')).toBe('api-secret')
  })
})

describe('apiClient.getJackettResults', () => {
  it('calls Jackett v2 results with Category[] and stored apikey', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(
        new Response(JSON.stringify({ Results: [] }), {
          headers: { 'Content-Type': 'application/json' },
        }),
      )

    await apiClient.getJackettResults('all', {
      query: 'matrix',
      'Category[]': ['2000', '5000'],
      is_serial: 1,
    })

    const called = String(fetchMock.mock.calls[0]?.[0])
    expect(called).toContain('/api/v2.0/indexers/all/results')
    expect(called).toContain('query=matrix')
    expect(called).toContain('is_serial=1')
    expect(called).toContain('Category%5B%5D=2000')
    expect(called).toContain('Category%5B%5D=5000')
    expect(called).toContain('apikey=api-secret')
    const headers = new Headers(fetchMock.mock.calls[0]?.[1]?.headers)
    expect(headers.get('X-Api-Key')).toBe('api-secret')
  })
})
