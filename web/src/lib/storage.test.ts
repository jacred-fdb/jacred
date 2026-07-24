import { beforeEach, describe, expect, it } from 'vitest'
import {
  getApiKey,
  getDevKey,
  getSurface,
  getTorrServerPassword,
  setApiKey,
  setDevKey,
  setSurface,
  setTorrServerCreds,
  StorageKeys,
} from '@/lib/storage'

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
  Object.defineProperty(globalThis, 'sessionStorage', {
    configurable: true,
    value: fakeStorage(),
  })
})

describe('secret storage', () => {
  it('persists API, Dev and TorrServer secrets in localStorage', () => {
    setApiKey('api')
    setDevKey('dev')
    setTorrServerCreds('http://localhost:8090', '', 'password')

    expect(getApiKey()).toBe('api')
    expect(getDevKey()).toBe('dev')
    expect(getTorrServerPassword()).toBe('password')
    expect(localStorage.getItem(StorageKeys.apiKey)).toBe('api')
    expect(localStorage.getItem(StorageKeys.devKey)).toBe('dev')
    expect(localStorage.getItem(StorageKeys.torrServerPassword)).toBe(
      'password',
    )
  })
})

describe('surface storage', () => {
  it('defaults to solid and persists glass', () => {
    expect(getSurface()).toBe('solid')
    setSurface('glass')
    expect(getSurface()).toBe('glass')
    expect(localStorage.getItem(StorageKeys.surface)).toBe('glass')
  })
})
