export type ConfigFieldType =
  | 'string'
  | 'int'
  | 'bool'
  | 'password'
  | 'stringList'
  | 'select'
  | 'json'

export type ConfigField = {
  key: string
  type: ConfigFieldType
  label: string
  description?: string | null
  sensitive?: boolean
  min?: number | null
  max?: number | null
  enumValues?: string[] | null
}

export type ConfigFieldChange = {
  path: string
  value: unknown
}

export type ConfigTracker = {
  id: string
  title: string
  fields?: ConfigField[] | null
}

export type ConfigGroup = {
  id: string
  title: string
  description?: string | null
  fields?: ConfigField[] | null
  trackers?: ConfigTracker[] | null
}

export type ConfigSchema = {
  groups?: ConfigGroup[] | null
}

export type ConfigFormat = 'yaml' | 'json'

export type ConfigGetResponse = {
  ok?: boolean
  path?: string
  format?: ConfigFormat | string
  displayFormat?: string
  exists?: boolean
  lastModifiedUtc?: string
  data?: Record<string, unknown>
  content?: string
  schema?: ConfigSchema
  sensitiveFields?: string[]
  note?: string
  error?: string
}

export type ConfigValidation = {
  ok?: boolean
  error?: string
  errors?: string[]
  warnings?: string[]
}

export type ConfigDiffEntry = {
  path?: string
  oldValue?: unknown
  newValue?: unknown
  sensitive?: boolean
  change?: string
}

export type ConfigDiffResponse = {
  ok?: boolean
  diffs?: ConfigDiffEntry[]
  changeCount?: number
  validation?: ConfigValidation
  error?: string
}

export type ConfigSaveRequest = {
  data?: Record<string, unknown>
  content?: string
  format?: ConfigFormat | string
}

const UNSAFE_KEYS = new Set(['__proto__', 'constructor', 'prototype'])

export function isSafeKey(key: string): boolean {
  return !!key && !UNSAFE_KEYS.has(key)
}

export function deepClone<T>(obj: T): T {
  return JSON.parse(JSON.stringify(obj ?? {})) as T
}

export function getByPath(obj: unknown, path: string): unknown {
  if (!obj || !path) return undefined
  const parts = path.split('.')
  let cur: unknown = obj
  for (const p of parts) {
    if (!isSafeKey(p) || cur == null || typeof cur !== 'object') return undefined
    cur = (cur as Record<string, unknown>)[p]
  }
  return cur
}

export function setByPath(
  obj: Record<string, unknown>,
  path: string,
  value: unknown,
): void {
  const parts = path.split('.')
  let cur: Record<string, unknown> = obj
  for (let i = 0; i < parts.length - 1; i++) {
    const p = parts[i]!
    if (!isSafeKey(p)) return
    const next = cur[p]
    if (next == null || typeof next !== 'object' || Array.isArray(next)) {
      cur[p] = {}
    }
    cur = cur[p] as Record<string, unknown>
  }
  const last = parts[parts.length - 1]!
  if (!isSafeKey(last)) return
  cur[last] = value
}

export function normalizeRaw(raw: unknown, field: ConfigField): unknown {
  if (raw == null) return raw
  if (!Array.isArray(raw)) return raw
  if (field.type === 'stringList' || field.type === 'json') return raw
  if (field.type === 'bool') return false
  if (field.type === 'int') return 0
  return ''
}

export function stringListToText(val: unknown): string {
  if (!Array.isArray(val)) return ''
  return val.join('\n')
}

export function textToStringList(text: string): string[] {
  return String(text || '')
    .split('\n')
    .map((s) => s.trim())
    .filter(Boolean)
}

export function parseJsonField(
  text: string,
  fieldKey: string,
  lastValid?: string,
): unknown {
  try {
    return JSON.parse(text || 'null')
  } catch {
    if (lastValid) {
      try {
        return JSON.parse(lastValid)
      } catch {
        /* fall through */
      }
    }
    return fieldKey.endsWith('.categories') || fieldKey.endsWith('categories')
      ? {}
      : []
  }
}

export function defaultJsonText(field: ConfigField, raw: unknown): string {
  if (raw != null) return JSON.stringify(raw, null, 2)
  return field.key.endsWith('categories') ? '{}' : '[]'
}

export function tabIdForGroup(group: ConfigGroup): string {
  return group.id === 'trackers' ? 'tab-trackers' : `tab-${group.id}`
}

export function resolveActiveTab(
  schema: ConfigSchema | null | undefined,
  activeTabId: string | null | undefined,
): string | null {
  if (!activeTabId || !schema?.groups?.length) return null
  const valid = new Set(schema.groups.map((g) => tabIdForGroup(g)))
  return valid.has(activeTabId) ? activeTabId : null
}

export function findFieldMeta(
  schema: ConfigSchema | null | undefined,
  path: string,
): ConfigField | null {
  if (!schema?.groups) return null
  for (const group of schema.groups) {
    if (group.id === 'trackers') {
      const [trackerName, ...rest] = path.split('.')
      const tracker = (group.trackers || []).find((t) => t.id === trackerName)
      if (tracker && rest.length) {
        return (
          (tracker.fields || []).find((f) => f.key === rest.join('.')) || null
        )
      }
    } else {
      const field = (group.fields || []).find((f) => f.key === path)
      if (field) return field
    }
  }
  return null
}

export function formatConfigValue(value: unknown, sensitive?: boolean): string {
  if (sensitive) return '••••••'
  if (value === undefined) return '—'
  if (value === null) return 'null'
  if (typeof value === 'string') return value
  try {
    return JSON.stringify(value)
  } catch {
    return String(value)
  }
}

export function formatMetaDate(
  iso: string | null | undefined,
  locale = 'ru-RU',
): string {
  if (!iso) return '—'
  try {
    return new Date(iso).toLocaleString(locale)
  } catch {
    return iso
  }
}

export type AuthErrorKind = 'network' | 'devkey' | 'apikey'

export function authErrorKind(status: number, hasDevKey: boolean): AuthErrorKind {
  if (status === 403) return 'network'
  if (status === 401) return hasDevKey ? 'apikey' : 'devkey'
  return 'network'
}
