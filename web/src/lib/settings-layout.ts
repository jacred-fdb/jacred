import type { ConfigField } from '@/lib/config-schema'

const WIDE_KEYS = new Set(['cookie', 'alias', 'aliasurl'])
const AUTH_KEYS = new Set(['login', 'password'])

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

/**
 * Split settings fields into layout sections so bools / compact inputs /
 * wide text / login+password are not mixed in one auto-flow grid.
 */
export function partitionSettingsFields(
  fields: ConfigField[] | null | undefined,
): SettingsFieldPartition {
  const bools: ConfigField[] = []
  const compact: ConfigField[] = []
  const wide: ConfigField[] = []
  const auth: ConfigField[] = []
  const authByKey = new Map<string, ConfigField>()

  for (const field of fields || []) {
    if (field.type === 'bool') {
      bools.push(field)
      continue
    }
    if (isWideField(field)) {
      wide.push(field)
      continue
    }
    if (AUTH_KEYS.has(field.key.toLowerCase())) {
      authByKey.set(field.key.toLowerCase(), field)
      continue
    }
    compact.push(field)
  }

  // Stable order: login then password when both exist
  for (const key of ['login', 'password'] as const) {
    const f = authByKey.get(key)
    if (f) auth.push(f)
  }

  return { bools, compact, wide, auth }
}
