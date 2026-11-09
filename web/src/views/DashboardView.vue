<script setup>
import { computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { useAppStore } from '../stores/app'

const store = useAppStore()

const healthLabel = computed(() => {
  if (store.loading) return '检查中'
  if (store.error) return '不可用'
  return store.health === 'Healthy' ? '正常' : String(store.health || '未知')
})

const modules = [
  { to: '/master-data', title: '主数据', text: '客户、供应商、物料、仓库、工艺参数' },
  { to: '/sales', title: '销售', text: '报价、订单、发货、应收' },
  { to: '/purchasing', title: '采购', text: '申请、订单、收货、应付' },
  { to: '/production', title: '生产', text: '打印任务、排产、材料耗用、后处理' },
  { to: '/inventory', title: '库存', text: '流水、调拨、盘点、可用量' },
  { to: '/finance', title: '财务', text: '总账、应收、应付、凭证' }
]

onMounted(store.loadOverview)
</script>

<template>
  <div class="space-y-6">
    <section class="overflow-hidden rounded-2xl bg-slate-950 p-6 text-white shadow-sm">
      <div class="max-w-3xl">
        <div class="text-xs font-semibold uppercase tracking-[0.25em] text-cyan-300">3dERP bootstrap</div>
        <h1 class="mt-3 text-2xl font-semibold lg:text-3xl">面向 3D 打印业务的 ERP 骨架</h1>
        <p class="mt-3 text-sm leading-6 text-slate-300">
          报价、订单、排产、材料、设备、后处理和财务将在同一个模块化单体中逐步落地。
          当前版本先验证前后端、数据库和 AgentOS 执行链路。
        </p>
      </div>
    </section>

    <div v-if="store.error" class="rounded-xl border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700">
      {{ store.error }}
    </div>

    <section class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <div class="rounded-xl border border-slate-200 bg-white p-4">
        <div class="text-xs text-slate-500">API</div>
        <div class="mt-2 text-lg font-semibold">.NET 10</div>
        <div class="mt-1 text-xs text-emerald-600">运行中</div>
      </div>
      <div class="rounded-xl border border-slate-200 bg-white p-4">
        <div class="text-xs text-slate-500">数据库</div>
        <div class="mt-2 text-lg font-semibold">SQL Server</div>
        <div class="mt-1 text-xs" :class="healthLabel === '正常' ? 'text-emerald-600' : 'text-amber-600'">
          {{ healthLabel }}
        </div>
      </div>
      <div class="rounded-xl border border-slate-200 bg-white p-4">
        <div class="text-xs text-slate-500">前端</div>
        <div class="mt-2 text-lg font-semibold">Vue 3</div>
        <div class="mt-1 text-xs text-slate-500">Vite · Pinia · Tailwind</div>
      </div>
      <div class="rounded-xl border border-slate-200 bg-white p-4">
        <div class="text-xs text-slate-500">环境</div>
        <div class="mt-2 text-lg font-semibold">{{ store.meta?.environment || 'Development' }}</div>
        <div class="mt-1 text-xs text-slate-500">{{ store.meta?.version || 'v0.1' }}</div>
      </div>
    </section>

    <section>
      <div class="mb-3 flex items-end justify-between">
        <div>
          <h2 class="text-base font-semibold">业务模块</h2>
          <p class="text-xs text-slate-500">先做窄而完整的流程，再逐模块扩展。</p>
        </div>
        <RouterLink class="text-xs font-medium text-cyan-700 hover:text-cyan-600" to="/master-data">
          从主数据开始 →
        </RouterLink>
      </div>
      <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        <RouterLink
          v-for="item in modules"
          :key="item.to"
          :to="item.to"
          class="group rounded-xl border border-slate-200 bg-white p-4 transition hover:-translate-y-0.5 hover:border-cyan-300 hover:shadow-sm"
        >
          <div class="flex items-center justify-between">
            <div class="font-medium">{{ item.title }}</div>
            <span class="text-slate-300 transition group-hover:text-cyan-500">→</span>
          </div>
          <p class="mt-2 text-xs leading-5 text-slate-500">{{ item.text }}</p>
        </RouterLink>
      </div>
    </section>
  </div>
</template>
