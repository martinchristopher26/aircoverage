<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const menuOpen = ref(false)

function closeMenu() {
  menuOpen.value = false
}

// Click anywhere else closes the avatar menu.
onMounted(() => document.addEventListener('click', closeMenu))
onUnmounted(() => document.removeEventListener('click', closeMenu))
</script>

<template>
  <header class="topbar">
    <div class="jf-mark"><span>J</span>F</div>
    <nav class="nav">
      <a class="active">Air Coverage</a>
      <a>My Items</a>
    </nav>
    <div class="topbar-right">
      <div class="avatar" title="Account" @click.stop="menuOpen = !menuOpen">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <circle cx="12" cy="8" r="4" />
          <path d="M4 21c0-4 3.6-7 8-7s8 3 8 7" />
        </svg>
        <div v-if="menuOpen" class="avatar-menu" @click.stop>
          <div class="who">
            <b>{{ auth.user?.displayName ?? 'Dev Team' }}</b>
            <small>{{ auth.user?.username }}</small>
          </div>
          <button @click="auth.logout()">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <path d="M16 17l5-5-5-5" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
            Sign out
          </button>
        </div>
      </div>
    </div>
  </header>
</template>
