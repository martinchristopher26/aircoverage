<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useItemsStore } from '../stores/items'

const items = useItemsStore()

const now = ref(Date.now())
let timer: number | undefined
onMounted(() => {
  items.loadSyncStatus()
  timer = window.setInterval(() => { now.value = Date.now(); items.loadSyncStatus() }, 15000)
})
onUnmounted(() => { if (timer) clearInterval(timer) })
const syncedAgo = computed(() => {
  if (!items.lastSync) return ''
  const secs = Math.max(0, Math.round((now.value - new Date(items.lastSync).getTime()) / 1000))
  return secs < 60 ? `synced ${secs}s ago` : `synced ${Math.round(secs / 60)}m ago`
})
</script>

<template>
  <div class="page-head">
    <h2>Air Coverage</h2>
    <button class="add-new" @click="items.openAdd()">
      <span class="plus">+</span> Add New
    </button>
    <div class="page-head-right">
      <span class="sync-note" v-if="syncedAgo">{{ syncedAgo }}</span>
      <span class="pico" title="Export">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z" />
          <path d="M14 3v6h6" />
        </svg>
      </span>
      <span class="pico help" title="Help">
        <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <circle cx="12" cy="12" r="9" />
          <path d="M9.2 9a2.8 2.8 0 0 1 5.4 1c0 1.8-2.6 2-2.6 4" />
          <line x1="12" y1="17.5" x2="12" y2="17.5" />
        </svg>
      </span>
    </div>
  </div>
</template>
