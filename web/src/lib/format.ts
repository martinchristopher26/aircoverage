const STATUS_COLORS: Record<string, string> = {
  New: '#3b73c4',
  'In Progress': '#1f9d58',
  Waiting: '#d8a72b',
  Resolved: '#2e8b57',
  Closed: '#9aa0a7',
}

export const PRIO_RANK: Record<string, number> = { Critical: 0, High: 1, Medium: 2, Low: 3 }

export function statusColor(s: string): string {
  return STATUS_COLORS[s] ?? '#9aa0a7'
}

/** "In Progress" -> "InProgress" to match the CSS class names. */
export function statusClass(s: string): string {
  return s.replace(/\s+/g, '')
}

export function isClosed(s: string): boolean {
  return s === 'Closed' || s === 'Resolved'
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

export function daysOpen(it: { received: string; status: string; updated: string | null }): number {
  const start = new Date(it.received).getTime()
  const end = it.status === 'Closed' && it.updated ? new Date(it.updated).getTime() : Date.now()
  return Math.max(0, Math.floor((end - start) / 86_400_000))
}

export function fmtDate(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' })
}

export function fmtDateTime(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}
