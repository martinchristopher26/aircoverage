import { defineStore } from 'pinia'
import { api, ApiError } from '../api/client'
import type { User } from '../types'

export const useAuthStore = defineStore('auth', {
  state: () => ({
    user: null as User | null,
    checked: false, // has the initial /me check completed?
    error: '',
    loading: false,
  }),

  getters: {
    authed: (state): boolean => state.user !== null,
  },

  actions: {
    /** Discover auth state on load (cookie present?). */
    async fetchMe() {
      try {
        this.user = await api.get<User>('/api/auth/me')
      } catch {
        this.user = null
      } finally {
        this.checked = true
      }
    },

    async login(username: string, password: string) {
      this.loading = true
      this.error = ''
      try {
        this.user = await api.post<User>('/api/auth/login', { username, password })
      } catch (e) {
        this.user = null
        this.error = e instanceof ApiError ? e.message : 'Sign in failed. Please try again.'
      } finally {
        this.loading = false
      }
    },

    async logout() {
      try {
        await api.post('/api/auth/logout')
      } catch {
        /* clear locally regardless */
      }
      this.user = null
    },
  },
})
