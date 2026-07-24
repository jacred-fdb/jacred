import { describe, expect, it } from 'vitest'
import type { ConfigField } from '@/lib/config-schema'
import { partitionSettingsFields } from '@/lib/settings-layout'

function field(
  key: string,
  type: ConfigField['type'],
  extras: Partial<ConfigField> = {},
): ConfigField {
  return { key, type, label: key, ...extras }
}

describe('partitionSettingsFields', () => {
  it('groups bools, compact, wide, and auth', () => {
    const parts = partitionSettingsFields([
      field('enable', 'bool'),
      field('host', 'string'),
      field('rq', 'int'),
      field('cookie', 'string'),
      field('login', 'string'),
      field('password', 'password'),
      field('meta', 'json'),
      field('tags', 'stringList'),
    ])

    expect(parts.bools.map((f) => f.key)).toEqual(['enable'])
    expect(parts.compact.map((f) => f.key)).toEqual(['host', 'rq'])
    expect(parts.wide.map((f) => f.key)).toEqual(['cookie', 'meta', 'tags'])
    expect(parts.auth.map((f) => f.key)).toEqual(['login', 'password'])
  })

  it('treats aliasurl as wide', () => {
    const parts = partitionSettingsFields([field('aliasurl', 'string')])
    expect(parts.wide.map((f) => f.key)).toEqual(['aliasurl'])
    expect(parts.compact).toEqual([])
  })
})
