import { describe, expect, it } from 'vitest'
import {
  authErrorKind,
  getByPath,
  setByPath,
  textToStringList,
} from '@/lib/config-schema'

describe('config schema helpers', () => {
  it('reads and writes nested paths', () => {
    const data: Record<string, unknown> = {}
    setByPath(data, 'tracker.login', 'user')
    expect(getByPath(data, 'tracker.login')).toBe('user')
  })

  it('normalizes newline lists', () => {
    expect(textToStringList('one\n two \none')).toEqual(['one', 'two', 'one'])
  })

  it('classifies config authorization failures', () => {
    expect(authErrorKind(403, false)).toBe('network')
    expect(authErrorKind(401, false)).toBe('devkey')
    expect(authErrorKind(401, true)).toBe('apikey')
  })
})
