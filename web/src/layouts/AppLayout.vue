<script setup>
import { computed, ref } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'

const route = useRoute()
const sidebarOpen = ref(false)

const nav = [
  { to: '/', label: '总览', mark: '01' },
  { to: '/master-data', label: '主数据', mark: '02' },
  { to: '/sales', label: '销售', mark: '03' },
  { to: '/purchasing', label: '采购', mark: '04' },
  { to: '/production', label: '生产', mark: '05' },
  { to: '/inventory', label: '库存', mark: '06' },
  { to: '/finance', label: '财务', mark: '07' },
  { to: '/settings', label: '系统设置', mark: '08' }
]

const pageTitle = computed(() => route.meta.title || '3dERP')
</script>

<template>
  <div class="min-h-screen bg-slate-50 text-slate-900">
    <div
      v-if="sidebarOpen"
      class="fixed inset-0 z-30 bg-slate-950/40 lg:hidden"
      @click="sidebarOpen = false"
    />

    <aside
      class="fixed inset-y-0 left-0 z-40 w-64 border-r border-slate-800 bg-slate-950 text-slate-100 transition-transform lg:translate-x-0"
      :class="sidebarOpen ? 'translate-x-0' : '-translate-x-full'"
    >
      <div class="flex h-16 items-center gap-3 border-b border-slate-800 px-5">
        <div class="grid h-9 w-9 place-items-center rounded-lg bg-cyan-400 font-bold text-slate-950">3D</div>
        <div>
          <div class="text-sm font-semibold tracking-wide">3dERP</div>
          <div class="text-[11px] text-slate-400">3D printing operations</div>
        </div>
      </div>

      <nav class="space-y-1 p-3">
        <RouterLink
          v-for="item in nav"
          :key="item.to"
          :to="item.to"
          class="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-slate-300 transition hover:bg-slate-800 hover:text-white"
          :class="{ 'bg-slate-800 text-white': route.path === item.to }"
          @click="sidebarOpen = false"
        >
          <span class="w-6 text-[10px] font-semibold text-cyan-300">{{ item.mark }}</span>
          {{ item.label }}
        </RouterLink>
      </nav>

      <div class="absolute inset-x-0 bottom-0 border-t border-slate-800 p-4 text-[11px] leading-5 text-slate-500">
        <div>modular monolith · v0.1</div>
        <div>SQL Server · .NET 10 · Vue 3</div>
      </div>
    </aside>

    <div class="lg:pl-64">
      <header class="sticky top-0 z-20 flex h-16 items-center justify-between border-b border-slate-200 bg-white/90 px-4 backdrop-blur lg:px-8">
        <div class="flex items-center gap-3">
          <button
            class="rounded-lg border border-slate-200 px-3 py-1.5 text-sm lg:hidden"
            type="button"
            @click="sidebarOpen = true"
          >
            菜单
          </button>
          <div>
            <div class="text-sm font-semibold">{{ pageTitle }}</div>
            <div class="text-[11px] text-slate-500">3dERP 管理后台</div>
          </div>
        </div>
        <div class="flex items-center gap-2 text-xs text-slate-600">
          <span class="h-2 w-2 rounded-full bg-emerald-500" />
          local development
        </div>
      </header>

      <main class="mx-auto max-w-7xl px-4 py-6 lg:px-8">
        <RouterView />
      </main>
    </div>
  </div>
</template>
