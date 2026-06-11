<script setup lang="ts">
import { useItemsStore } from '../stores/items'
import { TABS } from '../types'
import QueueTable from './QueueTable.vue'

const items = useItemsStore()
</script>

<template>
  <main class="content">
    <div class="card">
      <!-- filter -->
      <div class="filter-bar">
        <div class="filter-input">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polygon points="22 3 2 3 10 12.5 10 19 14 21 14 12.5 22 3" />
          </svg>
          <input v-model="items.search" type="text" placeholder="Filter by title, ID, source, or assignee…" />
        </div>
        <span class="kebab" title="More">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor">
            <circle cx="12" cy="5" r="1.7" />
            <circle cx="12" cy="12" r="1.7" />
            <circle cx="12" cy="19" r="1.7" />
          </svg>
        </span>
      </div>

      <!-- tabs -->
      <div class="tabs">
        <button
          v-for="t in TABS"
          :key="t.key"
          class="tab"
          :class="{ active: items.activeTab === t.key }"
          @click="items.setTab(t.key)"
        >
          {{ t.label }} <span class="cnt">{{ items.tabCount(t.key) }}</span>
        </button>
      </div>

      <!-- table -->
      <QueueTable />
    </div>
  </main>
</template>
