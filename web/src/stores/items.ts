import { defineStore } from 'pinia'
import { api } from '../api/client'
import { PRIO_RANK } from '../lib/format'
import type { Item, ItemDraft, Status, TicketType } from '../types'

type SortKey = 'priority' | 'received'

const CLOSED_SCOPE_TABS = ['Resolved', 'Closed', 'all']
/** Sentinel filter value matching items with no assignee. */
export const UNASSIGNED_FILTER = '__unassigned__'
function dedupeById(list: Item[]): Item[] {
  const seen = new Set<number>()
  return list.filter((i) => (seen.has(i.id) ? false : (seen.add(i.id), true)))
}

/** Server fields are nullable; normalize to '' so the UI deals only in strings. */
function normalize(raw: Record<string, unknown>): Item {
  return {
    id: raw.id as number,
    number: (raw.number as string) ?? '',
    title: (raw.title as string) ?? '',
    description: (raw.description as string) ?? '',
    priority: raw.priority as Item['priority'],
    status: raw.status as Status,
    requestedBy: (raw.requestedBy as string) ?? '',
    assignee: (raw.assignee as string) ?? '',
    ticketType: ((raw.ticketType as string) ?? '') as TicketType,
    ticketRef: (raw.ticketRef as string) ?? '',
    url: (raw.url as string) ?? '',
    received: raw.received as string,
    updated: (raw.updated as string | null) ?? null,
  }
}

function toInput(draft: ItemDraft) {
  return {
    title: draft.title.trim(),
    description: draft.description,
    priority: draft.priority,
    status: draft.status,
    requestedBy: draft.requestedBy,
    assignee: draft.assignee,
    ticketType: draft.ticketType,
    ticketRef: draft.ticketRef,
  }
}

export const useItemsStore = defineStore('items', {
  state: () => ({
    items: [] as Item[],
    loading: false,
    loadError: '',
    saving: false,

    // view state
    search: '',
    assigneeFilter: '',
    activeTab: 'open',
    sortKey: 'priority' as SortKey,
    sortDir: 1 as 1 | -1, // 1 = critical-first / oldest-first

    // closed/resolved items loaded on demand
    closedItems: [] as Item[],
    closedLoaded: false,
    lastSync: null as string | null,

    // modal state
    editing: null as ItemDraft | null,
    mode: 'edit' as 'edit' | 'add',
  }),

  getters: {
    filteredByTab(state): Item[] {
      const closedScope = CLOSED_SCOPE_TABS.includes(state.activeTab)
      const source = closedScope ? dedupeById([...state.items, ...state.closedItems]) : state.items
      return source.filter((it) => {
        if (state.activeTab === 'all') return true
        if (state.activeTab === 'open') return it.status !== 'Closed' && it.status !== 'Resolved'
        return it.status === state.activeTab
      })
    },

    filteredBySearch(): Item[] {
      const q = this.search.trim().toLowerCase()
      if (!q) return this.filteredByTab
      return this.filteredByTab.filter((it) =>
        [it.title, it.number, it.requestedBy, it.assignee, it.description].some((f) =>
          (f || '').toLowerCase().includes(q),
        ),
      )
    },

    /** Distinct assignee names present in the current tab (excludes unassigned). */
    assigneeOptions(): string[] {
      const set = new Set<string>()
      for (const it of this.filteredByTab) if (it.assignee) set.add(it.assignee)
      return [...set].sort((a, b) => a.localeCompare(b))
    },

    /** Whether the current tab contains any unassigned items. */
    hasUnassigned(): boolean {
      return this.filteredByTab.some((it) => !it.assignee)
    },

    filteredByAssignee(): Item[] {
      const f = this.assigneeFilter
      if (!f) return this.filteredBySearch
      if (f === UNASSIGNED_FILTER) return this.filteredBySearch.filter((it) => !it.assignee)
      return this.filteredBySearch.filter((it) => it.assignee === f)
    },

    visibleItems(): Item[] {
      const arr = this.filteredByAssignee.slice()
      const dir = this.sortDir
      const key = this.sortKey
      arr.sort((a, b) => {
        let cmp: number
        if (key === 'priority') {
          cmp = PRIO_RANK[a.priority] - PRIO_RANK[b.priority]
          if (cmp === 0) cmp = new Date(a.received).getTime() - new Date(b.received).getTime()
        } else {
          cmp = new Date(a.received).getTime() - new Date(b.received).getTime()
        }
        return cmp * dir
      })
      return arr
    },

    openCount: (state): number =>
      state.items.filter((it) => it.status !== 'Closed' && it.status !== 'Resolved').length,

    criticalOpen: (state): number =>
      state.items.filter(
        (it) => it.priority === 'Critical' && it.status !== 'Closed' && it.status !== 'Resolved',
      ).length,

    tabCount(): (key: string) => number {
      return (key: string) => {
        const closedScope = CLOSED_SCOPE_TABS.includes(key)
        const source = closedScope ? dedupeById([...this.items, ...this.closedItems]) : this.items
        if (key === 'all') return source.length
        if (key === 'open') return this.openCount
        return source.filter((it) => it.status === key).length
      }
    },
  },

  actions: {
    async loadClosed() {
      const data = await api.get<Record<string, unknown>[]>('/api/items?scope=closed')
      this.closedItems = data.map(normalize)
      this.closedLoaded = true
    },

    async loadSyncStatus() {
      try {
        const s = await api.get<{ lastSync: string | null }>('/api/sync/status')
        this.lastSync = s.lastSync
      } catch {
        /* ignore */
      }
    },

    async setTab(key: string) {
      this.activeTab = key
      if (CLOSED_SCOPE_TABS.includes(key) && !this.closedLoaded) {
        try { await this.loadClosed() } catch { this.loadError = 'Could not load closed items.' }
      }
    },

    async load() {
      this.closedLoaded = false
      this.closedItems = []
      this.loading = true
      this.loadError = ''
      try {
        const data = await api.get<Record<string, unknown>[]>('/api/items')
        this.items = data.map(normalize)
      } catch {
        this.loadError = 'Could not load the queue. Is the API running?'
      } finally {
        this.loading = false
      }
    },

    setSort(key: SortKey) {
      if (this.sortKey === key) this.sortDir = (this.sortDir * -1) as 1 | -1
      else {
        this.sortKey = key
        this.sortDir = 1
      }
    },

    openDetail(it: Item) {
      this.mode = 'edit'
      this.editing = JSON.parse(JSON.stringify(it)) as ItemDraft
    },

    openAdd() {
      this.mode = 'add'
      this.editing = {
        title: '',
        description: '',
        priority: 'Medium',
        status: 'New',
        requestedBy: '',
        assignee: '',
        ticketType: '',
        ticketRef: '',
        received: new Date().toISOString(),
        updated: null,
      }
    },

    closeModal() {
      this.editing = null
    },

    setStatus(s: Status) {
      if (this.editing) this.editing.status = s
    },

    placeItem(item: Item) {
      this.items = this.items.filter((i) => i.id !== item.id)
      this.closedItems = this.closedItems.filter((i) => i.id !== item.id)
      const isClosed = item.status === 'Closed' || item.status === 'Resolved'
      if (isClosed) {
        if (this.closedLoaded) this.closedItems.push(item)
      } else {
        this.items.push(item)
      }
    },

    async save() {
      if (!this.editing || !this.editing.title.trim()) return
      this.saving = true
      const payload = toInput(this.editing)
      try {
        if (this.mode === 'add') {
          const created = await api.post<Record<string, unknown>>('/api/items', payload)
          this.placeItem(normalize(created))
        } else {
          const updated = await api.put<Record<string, unknown>>(
            `/api/items/${this.editing.id}`,
            payload,
          )
          this.placeItem(normalize(updated))
        }
        this.closeModal()
      } finally {
        this.saving = false
      }
    },

    async remove() {
      const id = this.editing?.id
      if (id === undefined) {
        this.closeModal()
        return
      }
      await api.del(`/api/items/${id}`)
      this.items = this.items.filter((i) => i.id !== id)
      this.closedItems = this.closedItems.filter((i) => i.id !== id)
      this.closeModal()
    },
  },
})
