import { defineStore } from 'pinia'
import http from '../api/http'

export const useAppStore = defineStore('app', {
  state: () => ({
    meta: null,
    health: null,
    loading: false,
    error: ''
  }),
  actions: {
    async loadOverview() {
      this.loading = true
      this.error = ''
      try {
        const [meta, health] = await Promise.all([
          http.get('/api/v1/meta'),
          http.get('/health')
        ])
        this.meta = meta.data
        this.health = health.data
      } catch (error) {
        this.error = error.message
      } finally {
        this.loading = false
      }
    }
  }
})
