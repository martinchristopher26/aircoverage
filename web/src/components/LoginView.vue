<script setup lang="ts">
import { reactive } from 'vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const form = reactive({ user: '', pass: '' })

function submit() {
  auth.login(form.user.trim(), form.pass)
}
</script>

<template>
  <div class="login-wrap">
    <form class="login-card" @submit.prevent="submit">
      <div class="login-logo"><span style="font-style: italic">J</span>F</div>
      <h1>Air Coverage</h1>
      <div class="sub">Dev support queue &middot; sign in to continue</div>

      <div class="field">
        <label>Username</label>
        <input v-model.trim="form.user" type="text" autocomplete="username" placeholder="devteam" />
      </div>
      <div class="field">
        <label>Password</label>
        <input v-model="form.pass" type="password" autocomplete="current-password" placeholder="••••••••" />
      </div>

      <div class="login-err">{{ auth.error }}</div>
      <button type="submit" class="btn-primary" :disabled="auth.loading">
        {{ auth.loading ? 'Signing in…' : 'Sign in' }}
      </button>

      <!-- Dev convenience; remove in production. -->
      <div class="login-hint">
        Shared team login &middot; user <code>devteam</code> &middot; pass <code>aircoverage</code>
      </div>
    </form>
  </div>
</template>
