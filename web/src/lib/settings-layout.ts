import type { ConfigField } from '@/lib/config-schema'

/** Keys that always span full width in the field grid. */
export const SETTINGS_FULL_WIDTH_KEYS = new Set([
  'cookie',
  'alias',
  'aliasurl',
])

const WIDE_KEYS = new Set(['alias', 'aliasurl'])
const AUTH_KEYS = new Set(['login', 'password'])
const SECRET_KEYS = new Set(['login', 'password', 'cookie'])

export type SettingsFieldPartition = {
  bools: ConfigField[]
  compact: ConfigField[]
  wide: ConfigField[]
  auth: ConfigField[]
}

function isWideField(field: ConfigField): boolean {
  if (field.type === 'json' || field.type === 'stringList') return true
  return WIDE_KEYS.has(field.key.toLowerCase())
}

function isSecretField(field: ConfigField): boolean {
  if (field.type === 'password' || field.sensitive) return true
  return SECRET_KEYS.has(field.key.toLowerCase())
}

export function isSettingsFullWidthField(field: ConfigField): boolean {
  if (field.type === 'json' || field.type === 'stringList') return true
  return SETTINGS_FULL_WIDTH_KEYS.has(field.key.toLowerCase())
}

/**
 * Split settings fields into layout sections so bools / compact inputs /
 * wide text / secrets (keys, cookies, login+password) are not mixed in one
 * auto-flow grid.
 */
export function partitionSettingsFields(
  fields: ConfigField[] | null | undefined,
): SettingsFieldPartition {
  const bools: ConfigField[] = []
  const compact: ConfigField[] = []
  const wide: ConfigField[] = []
  const auth: ConfigField[] = []
  const authByKey = new Map<string, ConfigField>()
  const otherSecrets: ConfigField[] = []

  for (const field of fields || []) {
    if (field.type === 'bool') {
      bools.push(field)
      continue
    }
    // Secrets before wide so cookie/password stay with login credentials
    if (isSecretField(field)) {
      const key = field.key.toLowerCase()
      if (AUTH_KEYS.has(key)) {
        authByKey.set(key, field)
      } else {
        otherSecrets.push(field)
      }
      continue
    }
    if (isWideField(field)) {
      wide.push(field)
      continue
    }
    compact.push(field)
  }

  // Stable order: login then password when both exist, then other secrets
  for (const key of ['login', 'password'] as const) {
    const f = authByKey.get(key)
    if (f) auth.push(f)
  }
  auth.push(...otherSecrets)

  return { bools, compact, wide, auth }
}
