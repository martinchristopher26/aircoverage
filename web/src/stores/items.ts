import { defineStore } from 'pinia'
import { api } from '../api/client'
import { PRIO_RANK } from '../lib/format'
import type { Item, ItemDraft, Status, TicketType } from '../types'

type SortKey = 'priority' | 'received'

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
    activeTab: 'open',
    sortKey: 'priority' as SortKey,
    sortDir: 1 as 1 | -1, // 1 = critical-first / oldest-first

    // modal state
    editing: null as ItemDraft | null,
    mode: 'edit' as 'edit' | 'add',
  }),

  getters: {
    filteredByTab(state): Item[] {
      return state.items.filter((it) => {
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

    visibleItems(): Item[] {
      const arr = this.filteredBySearch.slice()
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
        if (key === 'all') return this.items.length
        if (key === 'open') return this.openCount
        return this.items.filter((it) => it.status === key).length
      }
    },
  },

  actions: {
    async load() {
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

    async save() {
      if (!this.editing || !this.editing.title.trim()) return
      this.saving = true
      const payload = toInput(this.editing)
      try {
        if (this.mode === 'add') {
          const created = await api.post<Record<string, unknown>>('/api/items', payload)
          this.items.push(normalize(created))
        } else {
          const updated = await api.put<Record<string, unknown>>(
            `/api/items/${this.editing.id}`,
            payload,
          )
          const normalized = normalize(updated)
          const idx = this.items.findIndex((i) => i.id === normalized.id)
          if (idx !== -1) this.items.splice(idx, 1, normalized)
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
      this.closeModal()
    },
  },
})
