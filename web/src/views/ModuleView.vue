<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const title = computed(() => route.meta.title || '模块')
const description = computed(() => route.meta.description || '模块建设中。')

const nextSteps = [
  '实体与数据库迁移',
  'API 契约与权限',
  '列表 / 表单 / 详情页',
  '集成测试与验收'
]
</script>

<template>
  <div class="space-y-5">
    <div class="rounded-2xl border border-slate-200 bg-white p-6">
      <div class="text-xs font-semibold uppercase tracking-[0.2em] text-cyan-600">module</div>
      <h1 class="mt-2 text-2xl font-semibold">{{ title }}</h1>
      <p class="mt-2 max-w-2xl text-sm leading-6 text-slate-500">{{ description }}</p>
    </div>

    <div class="grid gap-4 lg:grid-cols-[1.2fr_0.8fr]">
      <div class="rounded-xl border border-slate-200 bg-white p-5">
        <h2 class="text-sm font-semibold">当前状态</h2>
        <div class="mt-4 rounded-lg border border-dashed border-slate-300 bg-slate-50 p-8 text-center">
          <div class="text-sm font-medium text-slate-700">该模块尚未实现</div>
          <p class="mt-2 text-xs text-slate-500">
            骨架已经就绪，下一步由 AgentOS 生成实体、API 和页面。
          </p>
        </div>
      </div>

      <div class="rounded-xl border border-slate-200 bg-white p-5">
        <h2 class="text-sm font-semibold">落地顺序</h2>
        <ol class="mt-4 space-y-3">
          <li v-for="(item, index) in nextSteps" :key="item" class="flex gap-3 text-sm">
            <span class="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-slate-100 text-xs font-semibold text-slate-600">
              {{ index + 1 }}
            </span>
            <span class="text-slate-600">{{ item }}</span>
          </li>
        </ol>
      </div>
    </div>
  </div>
</template>
