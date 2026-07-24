import type { Component } from 'vue'
import {
  AudioLines,
  Database,
  HardDrive,
  Layers,
  Plug,
  RefreshCw,
  ScrollText,
  Search,
  Shield,
  SlidersHorizontal,
  Zap,
} from '@lucide/vue'

/** Lucide counterparts of legacy Bootstrap group icons (settings-form.js). */
const GROUP_ICONS: Record<string, Component> = {
  server: HardDrive,
  api: Plug,
  sync: RefreshCw,
  logging: ScrollText,
  tracks: AudioLines,
  fdb: Database,
  evercache: Zap,
  search: Search,
  proxy: Shield,
  trackers: Layers,
}

export function settingsGroupIcon(groupId: string): Component {
  return GROUP_ICONS[groupId] || SlidersHorizontal
}
