<script setup lang="ts">
import { onMounted, onUnmounted, watch } from 'vue'
import { useAuthStore } from './stores/auth'
import { useItemsStore } from './stores/items'
import LoginView from './components/LoginView.vue'
import TopBar from './components/TopBar.vue'
import PageHeader from './components/PageHeader.vue'
import QueueCard from './components/QueueCard.vue'
import ItemModal from './components/ItemModal.vue'

const auth = useAuthStore()
const items = useItemsStore()

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') items.closeModal()
}

// Load the queue whenever we become authenticated (initial cookie check or login).
watch(
  () => auth.authed,
  (isAuthed) => {
    if (isAuthed) items.load()
  },
)

onMounted(() => {
  auth.fetchMe()
  window.addEventListener('keydown', onKeydown)
})
onUnmounted(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div v-if="!auth.checked" class="boot" />
  <LoginView v-else-if="!auth.authed" />
  <template v-else>
    <TopBar />
    <PageHeader />
    <QueueCard />
    <ItemModal v-if="items.editing" />
  </template>
</template>
