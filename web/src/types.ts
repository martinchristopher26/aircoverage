export const PRIORITIES = ['Critical', 'High', 'Medium', 'Low'] as const
export const STATUSES = ['New', 'In Progress', 'Waiting', 'Resolved', 'Closed'] as const
export const DEVS = ['Alex Reyes', 'Priya Shah', 'Marcus Tran', 'Dana Kim', 'Sam Whitfield'] as const

export type Priority = (typeof PRIORITIES)[number]
export type Status = (typeof STATUSES)[number]
export type TicketType = '' | 'ADO' | 'ConnectWise'

/** An item as held in the SPA (nullable server fields normalized to ''). */
export interface Item {
  id: number
  number: string
  title: string
  description: string
  priority: Priority
  status: Status
  requestedBy: string
  assignee: string
  ticketType: TicketType
  ticketRef: string
  url: string
  received: string
  updated: string | null
}

/** Working copy used by the modal. New items have no id/number until created. */
export interface ItemDraft {
  id?: number
  number?: string
  title: string
  description: string
  priority: Priority
  status: Status
  requestedBy: string
  assignee: string
  ticketType: TicketType
  ticketRef: string
  url?: string
  received: string
  updated: string | null
}

export interface User {
  username: string
  displayName: string
}

export interface QueueTab {
  key: string
  label: string
}

export const TABS: QueueTab[] = [
  { key: 'open', label: 'Open' },
  { key: 'New', label: 'New' },
  { key: 'In Progress', label: 'In Progress' },
  { key: 'Waiting', label: 'Waiting' },
  { key: 'Resolved', label: 'Resolved' },
  { key: 'Closed', label: 'Closed' },
  { key: 'all', label: 'All' },
]
