<script setup lang="ts">
import { computed } from 'vue'
import { useItemsStore } from '../stores/items'
import { DEVS, PRIORITIES, STATUSES } from '../types'
import { daysOpen, fmtDate, fmtDateTime, statusColor } from '../lib/format'

const items = useItemsStore()

// Non-null view of the working copy (the modal only renders when editing is set).
const draft = computed(() => items.editing!)
</script>

<template>
  <div class="overlay" @click.self="items.closeModal()">
    <div class="modal">
      <div class="modal-head">
        <div style="flex: 1">
          <div v-if="items.mode === 'edit'" class="mh-id">{{ draft.number }}</div>
          <div v-else class="mh-id">NEW ITEM</div>
          <h3 v-if="items.mode === 'edit'">{{ draft.title }}</h3>
          <h3 v-else>
            <input v-model="draft.title" type="text" placeholder="Short summary of the air coverage item" />
          </h3>
        </div>
        <button class="modal-x" @click="items.closeModal()">×</button>
      </div>

      <div class="modal-body">
        <!-- status flow (edit mode) -->
        <div v-if="items.mode === 'edit'" class="mfield full" style="margin-bottom: 20px">
          <div class="flow-label">Status — click to advance</div>
          <div class="status-flow">
            <button
              v-for="s in STATUSES"
              :key="s"
              class="flow-btn"
              :class="{ cur: draft.status === s }"
              @click="items.setStatus(s)"
            >
              <span class="sdot" :style="{ background: statusColor(s) }" />{{ s }}
            </button>
          </div>
        </div>

        <div class="grid2">
          <div v-if="items.mode === 'add'" class="mfield">
            <label>Status</label>
            <select v-model="draft.status">
              <option v-for="s in STATUSES" :key="s">{{ s }}</option>
            </select>
          </div>
          <div class="mfield">
            <label>Priority</label>
            <select v-model="draft.priority">
              <option v-for="p in PRIORITIES" :key="p">{{ p }}</option>
            </select>
          </div>
          <div class="mfield">
            <label>Assigned developer</label>
            <select v-model="draft.assignee">
              <option value="">Unassigned</option>
              <option v-for="d in DEVS" :key="d">{{ d }}</option>
            </select>
          </div>
          <div class="mfield">
            <label>Requested by / source</label>
            <input v-model.trim="draft.requestedBy" type="text" placeholder="e.g. Support, Customer, On-call" />
          </div>
          <div class="mfield">
            <label>Linked ticket</label>
            <div class="ticket-split">
              <select v-model="draft.ticketType">
                <option value="">None</option>
                <option>ADO</option>
                <option>ConnectWise</option>
              </select>
              <input v-model.trim="draft.ticketRef" type="text" :disabled="!draft.ticketType" placeholder="#12345" />
            </div>
          </div>
          <div class="mfield full">
            <label>Description / details</label>
            <textarea v-model="draft.description" placeholder="What's going on, repro steps, impact, who's affected…" />
          </div>
        </div>

        <div v-if="items.mode === 'edit'" class="grid2" style="margin-top: 16px">
          <div class="mfield">
            <label>Received</label>
            <div class="meta-read">
              {{ fmtDate(draft.received) }} <span class="muted">· {{ daysOpen(draft) }} days open</span>
            </div>
          </div>
          <div class="mfield">
            <label>Last updated</label>
            <div class="meta-read">{{ draft.updated ? fmtDateTime(draft.updated) : '—' }}</div>
          </div>
        </div>
      </div>

      <div class="modal-foot">
        <button v-if="items.mode === 'edit'" class="btn danger-ghost" @click="items.remove()">Delete</button>
        <span v-if="items.mode === 'edit'" class="updated-note">Item {{ draft.number }}</span>
        <button class="btn" @click="items.closeModal()">Cancel</button>
        <button class="btn green" :disabled="!draft.title.trim() || items.saving" @click="items.save()">
          {{ items.mode === 'add' ? 'Create item' : 'Save changes' }}
        </button>
      </div>
    </div>
  </div>
</template>
