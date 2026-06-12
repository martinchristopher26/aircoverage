<script setup lang="ts">
import { useItemsStore } from '../stores/items'
import { daysOpen, initials, isClosed, statusClass } from '../lib/format'

const items = useItemsStore()
</script>

<template>
  <div class="table-scroll">
    <table>
      <thead>
        <tr>
          <th class="sortable" style="width: 130px" @click="items.setSort('priority')">
            Priority<span v-if="items.sortKey === 'priority'" class="arrow">{{ items.sortDir > 0 ? '↑' : '↓' }}</span>
          </th>
          <th style="width: 120px">Item #</th>
          <th>Title / Summary</th>
          <th style="width: 150px">Status</th>
          <th style="width: 170px">Assigned</th>
          <th style="width: 140px">Source</th>
          <th style="width: 120px">Ticket</th>
          <th class="sortable" style="width: 120px" @click="items.setSort('received')">
            Days Open<span v-if="items.sortKey === 'received'" class="arrow">{{ items.sortDir > 0 ? '↑' : '↓' }}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="it in items.visibleItems" :key="it.id" style="cursor: pointer" @click="items.openDetail(it)">
          <td><span class="prio" :class="it.priority"><span class="dot" />{{ it.priority }}</span></td>
          <td><span class="id-link" @click.stop="items.openDetail(it)">{{ it.number }}</span></td>
          <td>
            <div class="row-title">
              {{ it.title }}
              <div v-if="it.description" class="desc-1">{{ it.description }}</div>
            </div>
          </td>
          <td><span class="status" :class="statusClass(it.status)"><span class="sdot" />{{ it.status }}</span></td>
          <td>
            <span v-if="it.assignee" class="pill assigned"><span class="av">{{ initials(it.assignee) }}</span>{{ it.assignee }}</span>
            <span v-else class="pill unassigned">Unassigned</span>
          </td>
          <td><span class="meta-read">{{ it.requestedBy || '—' }}</span></td>
          <td>
            <a v-if="it.ticketRef" class="ticket-link" href="#" @click.stop.prevent>
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1" />
                <path d="M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1" />
              </svg>
              {{ it.ticketType }} {{ it.ticketRef }}
            </a>
            <span v-else class="ticket-link none">—</span>
          </td>
          <td>
            <span class="days" :class="{ 'age-warn': daysOpen(it) >= 14 && !isClosed(it.status) }">
              {{ daysOpen(it) }} <small>{{ daysOpen(it) === 1 ? 'day' : 'days' }}</small>
            </span>
          </td>
        </tr>
        <tr v-if="items.visibleItems.length === 0">
          <td colspan="8">
            <div class="empty">
              <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6">
                <path d="M9 11l3 3L22 4" />
                <path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" />
              </svg>
              <p>Nothing here. {{ items.search ? 'No items match your filter.' : 'The queue is clear.' }}</p>
            </div>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
