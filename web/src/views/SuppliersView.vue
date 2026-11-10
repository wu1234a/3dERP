<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import {
  createSupplier,
  deactivateSupplier,
  fetchSuppliers,
  updateSupplier
} from '../api/suppliers'

const route = useRoute()

const PAGE_SIZE_OPTIONS = [10, 20, 50]
const CODE_PATTERN = /^[A-Z0-9-]+$/
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const FIELD_LIMITS = {
  code: 32,
  name: 128,
  contactName: 64,
  phone: 32,
  email: 128
}

// ---------- 列表状态 ----------
const items = ref([])
const page = ref(1)
const pageSize = ref(10)
const totalCount = ref(0)
const loading = ref(false)
const loadError = ref('')
const keywordInput = ref('')
const keyword = ref('')
const includeInactive = ref(false)

const totalPages = computed(() =>
  pageSize.value > 0 ? Math.max(1, Math.ceil(totalCount.value / pageSize.value)) : 1
)

const isEmpty = computed(
  () => !loading.value && !loadError.value && items.value.length === 0
)

async function loadSuppliers() {
  loading.value = true
  loadError.value = ''
  try {
    const result = await fetchSuppliers({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value,
      includeInactive: includeInactive.value
    })
    items.value = result.items ?? []
    totalCount.value = result.totalCount ?? 0
    // 删除/停用后当前页可能越界，回退到最后一页重新加载。
    if (items.value.length === 0 && result.page > 1 && result.page > (result.totalPages ?? 0)) {
      page.value = Math.max(1, result.totalPages ?? 1)
      await loadSuppliers()
    }
  } catch (error) {
    loadError.value = error.message || '加载供应商列表失败'
    items.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

function search() {
  keyword.value = keywordInput.value.trim()
  page.value = 1
  loadSuppliers()
}

function resetSearch() {
  keywordInput.value = ''
  keyword.value = ''
  includeInactive.value = false
  page.value = 1
  loadSuppliers()
}

function toggleIncludeInactive() {
  page.value = 1
  loadSuppliers()
}

function changePage(next) {
  if (loading.value || next < 1 || next > totalPages.value) return
  page.value = next
  loadSuppliers()
}

function changePageSize() {
  page.value = 1
  loadSuppliers()
}

function formatDateTime(value) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? '—'
    : date.toLocaleString('zh-CN', { hour12: false })
}

// ---------- 新建 / 编辑对话框 ----------
const dialogOpen = ref(false)
const saving = ref(false)
const dialogError = ref('')
const editingId = ref(null)

const form = reactive({
  code: '',
  name: '',
  contactName: '',
  phone: '',
  email: ''
})

const formErrors = reactive({})

const dialogTitle = computed(() => (editingId.value ? '编辑供应商' : '新建供应商'))

function resetForm() {
  Object.assign(form, {
    code: '',
    name: '',
    contactName: '',
    phone: '',
    email: ''
  })
  clearFormErrors()
}

function clearFormErrors() {
  for (const key of Object.keys(formErrors)) {
    delete formErrors[key]
  }
  dialogError.value = ''
}

function openCreateDialog() {
  editingId.value = null
  resetForm()
  dialogOpen.value = true
}

function openEditDialog(row) {
  editingId.value = row.id
  Object.assign(form, {
    code: row.code ?? '',
    name: row.name ?? '',
    contactName: row.contactName ?? '',
    phone: row.phone ?? '',
    email: row.email ?? ''
  })
  clearFormErrors()
  dialogOpen.value = true
}

function closeDialog() {
  if (saving.value) return
  dialogOpen.value = false
}

function validateForm() {
  clearFormErrors()

  const code = form.code.trim()
  if (!code) {
    formErrors.code = '供应商编码必填。'
  } else if (code.length < 2 || code.length > FIELD_LIMITS.code) {
    formErrors.code = `供应商编码长度必须在 2-${FIELD_LIMITS.code} 位之间。`
  } else if (!CODE_PATTERN.test(code)) {
    formErrors.code = '供应商编码只能包含大写字母、数字和短横线。'
  }

  const name = form.name.trim()
  if (!name) {
    formErrors.name = '供应商名称必填。'
  } else if (name.length > FIELD_LIMITS.name) {
    formErrors.name = `供应商名称长度不能超过 ${FIELD_LIMITS.name} 个字符。`
  }

  if (form.contactName.trim().length > FIELD_LIMITS.contactName) {
    formErrors.contactName = `联系人长度不能超过 ${FIELD_LIMITS.contactName} 个字符。`
  }

  if (form.phone.trim().length > FIELD_LIMITS.phone) {
    formErrors.phone = `电话长度不能超过 ${FIELD_LIMITS.phone} 个字符。`
  }

  const email = form.email.trim()
  if (email.length > FIELD_LIMITS.email) {
    formErrors.email = `邮箱长度不能超过 ${FIELD_LIMITS.email} 个字符。`
  } else if (email && !EMAIL_PATTERN.test(email)) {
    formErrors.email = '邮箱格式不正确。'
  }

  return Object.keys(formErrors).length === 0
}

async function submitForm() {
  if (saving.value) return
  if (!validateForm()) return

  saving.value = true
  try {
    const payload = {
      code: form.code.trim(),
      name: form.name.trim(),
      contactName: form.contactName.trim() || null,
      phone: form.phone.trim() || null,
      email: form.email.trim() || null
    }
    if (editingId.value) {
      await updateSupplier(editingId.value, payload)
    } else {
      await createSupplier(payload)
    }
    dialogOpen.value = false
    page.value = 1
    await loadSuppliers()
  } catch (error) {
    applyServerErrors(error)
  } finally {
    saving.value = false
  }
}

// 优先展示后端 ValidationProblem 的字段级错误（400），
// 其余（409 重复编码等）直接展示 detail / title。
function applyServerErrors(error) {
  const problem = error.response?.data
  const errors = problem?.errors
  if (errors && typeof errors === 'object') {
    for (const [field, messages] of Object.entries(errors)) {
      const message = Array.isArray(messages) ? messages[0] : String(messages)
      if (field in form) {
        formErrors[field] = message
      } else {
        dialogError.value = dialogError.value || message
      }
    }
    if (!dialogError.value && Object.keys(errors).length > 0) {
      dialogError.value = '请修正表单中的错误后重试。'
    }
    return
  }
  dialogError.value = error.message || '保存失败，请稍后重试。'
}

// ---------- 停用（软删除） ----------
const pendingDeactivate = ref(null)
const deactivating = ref(false)
const deactivateError = ref('')

function askDeactivate(row) {
  deactivateError.value = ''
  pendingDeactivate.value = row
}

function cancelDeactivate() {
  if (deactivating.value) return
  pendingDeactivate.value = null
}

async function confirmDeactivate() {
  if (!pendingDeactivate.value || deactivating.value) return
  deactivating.value = true
  try {
    await deactivateSupplier(pendingDeactivate.value.id)
    pendingDeactivate.value = null
    await loadSuppliers()
  } catch (error) {
    deactivateError.value = error.message || '停用失败，请稍后重试。'
  } finally {
    deactivating.value = false
  }
}

onMounted(loadSuppliers)
</script>

<template>
  <div class="space-y-5">
    <!-- 页头 -->
    <div class="rounded-2xl border border-slate-200 bg-white p-6">
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div class="text-xs font-semibold uppercase tracking-[0.2em] text-cyan-600">master data</div>
          <h1 class="mt-2 text-2xl font-semibold">供应商主数据</h1>
          <p class="mt-2 max-w-2xl text-sm leading-6 text-slate-500">
            树脂、金属粉末、线材等 3D 打印物料的供应商主数据。供应商编码全局唯一，供后续采购模块引用。
            停用为软删除，不物理删除数据。
          </p>
        </div>
        <button
          type="button"
          class="rounded-lg bg-cyan-500 px-4 py-2 text-sm font-medium text-white transition hover:bg-cyan-600 disabled:cursor-not-allowed disabled:opacity-60"
          :disabled="loading"
          @click="openCreateDialog"
        >
          新建供应商
        </button>
      </div>

      <!-- 主数据子导航 -->
      <nav class="mt-4 flex flex-wrap gap-2 text-sm">
        <RouterLink
          to="/master-data"
          class="rounded-lg px-3 py-1.5 transition"
          :class="route.path === '/master-data' ? 'bg-white text-cyan-700 shadow-sm ring-1 ring-cyan-200' : 'text-slate-500 hover:text-slate-700'"
        >
          客户
        </RouterLink>
        <RouterLink
          to="/master-data/materials"
          class="rounded-lg px-3 py-1.5 transition"
          :class="route.path === '/master-data/materials' ? 'bg-white text-cyan-700 shadow-sm ring-1 ring-cyan-200' : 'text-slate-500 hover:text-slate-700'"
        >
          物料
        </RouterLink>
        <RouterLink
          to="/master-data/suppliers"
          class="rounded-lg px-3 py-1.5 transition"
          :class="route.path === '/master-data/suppliers' ? 'bg-white text-cyan-700 shadow-sm ring-1 ring-cyan-200' : 'text-slate-500 hover:text-slate-700'"
        >
          供应商
        </RouterLink>
        <RouterLink
          to="/master-data/warehouses"
          class="rounded-lg px-3 py-1.5 transition"
          :class="route.path === '/master-data/warehouses' ? 'bg-white text-cyan-700 shadow-sm ring-1 ring-cyan-200' : 'text-slate-500 hover:text-slate-700'"
        >
          仓库
        </RouterLink>
      </nav>
    </div>

    <!-- 搜索 + 列表 -->
    <div class="rounded-2xl border border-slate-200 bg-white">
      <form class="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4" @submit.prevent="search">
        <input
          v-model="keywordInput"
          type="search"
          class="w-full max-w-xs rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-100"
          placeholder="按编码 / 名称 / 联系人 / 电话 / 邮箱搜索"
        />
        <button
          type="submit"
          class="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
          :disabled="loading"
        >
          搜索
        </button>
        <button
          type="button"
          class="rounded-lg px-3 py-2 text-sm text-slate-500 transition hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-60"
          :disabled="loading"
          @click="resetSearch"
        >
          重置
        </button>
        <label class="ml-auto flex items-center gap-2 text-sm text-slate-600">
          <input
            v-model="includeInactive"
            type="checkbox"
            class="h-4 w-4 rounded border-slate-300 accent-cyan-500"
            :disabled="loading"
            @change="toggleIncludeInactive"
          />
          包含已停用
        </label>
      </form>

      <!-- 加载失败 -->
      <div v-if="loadError" class="p-6">
        <div class="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          <span>{{ loadError }}</span>
          <button
            type="button"
            class="rounded-lg border border-red-300 px-3 py-1.5 text-xs font-medium transition hover:bg-red-100"
            @click="loadSuppliers"
          >
            重试
          </button>
        </div>
      </div>

      <!-- 加载中 -->
      <div v-else-if="loading" class="flex items-center justify-center gap-3 p-12 text-sm text-slate-500">
        <span class="h-4 w-4 animate-spin rounded-full border-2 border-slate-300 border-t-cyan-500" />
        正在加载供应商数据…
      </div>

      <!-- 空数据 -->
      <div v-else-if="isEmpty" class="p-6">
        <div class="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-10 text-center">
          <div class="text-sm font-medium text-slate-700">
            {{ keyword || includeInactive ? '没有符合条件的供应商' : '还没有供应商' }}
          </div>
          <p class="mt-2 text-xs text-slate-500">
            {{ keyword || includeInactive ? '调整关键字或筛选条件后重新搜索。' : '点击右上角「新建供应商」创建第一个供应商。' }}
          </p>
        </div>
      </div>

      <!-- 数据表格 -->
      <div v-else class="overflow-x-auto">
        <table class="w-full text-left text-sm">
          <thead>
            <tr class="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-500">
              <th class="px-4 py-3 font-medium">编码</th>
              <th class="px-4 py-3 font-medium">名称</th>
              <th class="px-4 py-3 font-medium">联系人</th>
              <th class="px-4 py-3 font-medium">电话</th>
              <th class="px-4 py-3 font-medium">邮箱</th>
              <th class="px-4 py-3 font-medium">状态</th>
              <th class="px-4 py-3 font-medium">更新时间</th>
              <th class="px-4 py-3 text-right font-medium">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in items"
              :key="row.id"
              class="border-b border-slate-50 transition last:border-0 hover:bg-slate-50/70"
            >
              <td class="whitespace-nowrap px-4 py-3 font-mono text-xs font-semibold text-slate-800">
                {{ row.code }}
              </td>
              <td class="px-4 py-3 font-medium text-slate-800">{{ row.name }}</td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-600">{{ row.contactName || '—' }}</td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-600">{{ row.phone || '—' }}</td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-600">{{ row.email || '—' }}</td>
              <td class="whitespace-nowrap px-4 py-3">
                <span
                  class="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium"
                  :class="row.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'"
                >
                  <span class="h-1.5 w-1.5 rounded-full" :class="row.isActive ? 'bg-emerald-500' : 'bg-slate-400'" />
                  {{ row.isActive ? '启用' : '已停用' }}
                </span>
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-xs text-slate-500">
                {{ formatDateTime(row.updatedAtUtc) }}
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-right">
                <button
                  type="button"
                  class="rounded-lg px-2.5 py-1 text-xs font-medium text-slate-600 transition hover:bg-slate-100 hover:text-slate-900"
                  @click="openEditDialog(row)"
                >
                  编辑
                </button>
                <button
                  v-if="row.isActive"
                  type="button"
                  class="rounded-lg px-2.5 py-1 text-xs font-medium text-red-600 transition hover:bg-red-50"
                  @click="askDeactivate(row)"
                >
                  停用
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 分页 -->
      <div
        v-if="!loadError"
        class="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-4 py-3 text-xs text-slate-500"
      >
        <div class="flex items-center gap-2">
          <span>每页</span>
          <select
            v-model.number="pageSize"
            class="rounded-lg border border-slate-200 px-2 py-1 text-xs outline-none focus:border-cyan-400"
            :disabled="loading"
            @change="changePageSize"
          >
            <option v-for="option in PAGE_SIZE_OPTIONS" :key="option" :value="option">
              {{ option }}
            </option>
          </select>
          <span>条，共 {{ totalCount }} 条</span>
        </div>
        <div class="flex items-center gap-2">
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-3 py-1.5 font-medium transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            :disabled="loading || page <= 1"
            @click="changePage(page - 1)"
          >
            上一页
          </button>
          <span>第 {{ page }} / {{ totalPages }} 页</span>
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-3 py-1.5 font-medium transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            :disabled="loading || page >= totalPages"
            @click="changePage(page + 1)"
          >
            下一页
          </button>
        </div>
      </div>
    </div>

    <!-- 新建 / 编辑对话框 -->
    <div
      v-if="dialogOpen"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/50 p-4"
      @click.self="closeDialog"
    >
      <div class="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-2xl bg-white shadow-xl">
        <div class="flex items-center justify-between border-b border-slate-100 px-6 py-4">
          <div>
            <h2 class="text-base font-semibold">{{ dialogTitle }}</h2>
            <p class="mt-0.5 text-xs text-slate-500">编码仅支持大写字母、数字与短横线，创建后全局唯一。</p>
          </div>
          <button
            type="button"
            class="rounded-lg p-1.5 text-slate-400 transition hover:bg-slate-100 hover:text-slate-600"
            :disabled="saving"
            @click="closeDialog"
          >
            ✕
          </button>
        </div>

        <form class="grid gap-4 px-6 py-5 sm:grid-cols-2" @submit.prevent="submitForm">
          <div v-if="dialogError" class="sm:col-span-2">
            <div class="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
              {{ dialogError }}
            </div>
          </div>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">供应商编码 *</span>
            <input
              v-model="form.code"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.code ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.code"
              placeholder="SUP-001"
              :disabled="saving"
            />
            <span v-if="formErrors.code" class="mt-1 block text-xs text-red-600">{{ formErrors.code }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">供应商名称 *</span>
            <input
              v-model="form.name"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.name ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.name"
              placeholder="示例供应商有限公司"
              :disabled="saving"
            />
            <span v-if="formErrors.name" class="mt-1 block text-xs text-red-600">{{ formErrors.name }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">联系人</span>
            <input
              v-model="form.contactName"
              type="text"
              class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-100"
              :maxlength="FIELD_LIMITS.contactName"
              :disabled="saving"
            />
            <span v-if="formErrors.contactName" class="mt-1 block text-xs text-red-600">{{ formErrors.contactName }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">电话</span>
            <input
              v-model="form.phone"
              type="tel"
              class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-100"
              :maxlength="FIELD_LIMITS.phone"
              :disabled="saving"
            />
            <span v-if="formErrors.phone" class="mt-1 block text-xs text-red-600">{{ formErrors.phone }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">邮箱</span>
            <input
              v-model="form.email"
              type="email"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.email ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.email"
              placeholder="contact@example.com"
              :disabled="saving"
            />
            <span v-if="formErrors.email" class="mt-1 block text-xs text-red-600">{{ formErrors.email }}</span>
          </label>

          <div class="flex justify-end gap-3 border-t border-slate-100 pt-4 sm:col-span-2">
            <button
              type="button"
              class="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
              :disabled="saving"
              @click="closeDialog"
            >
              取消
            </button>
            <button
              type="submit"
              class="rounded-lg bg-cyan-500 px-4 py-2 text-sm font-medium text-white transition hover:bg-cyan-600 disabled:cursor-not-allowed disabled:opacity-60"
              :disabled="saving"
            >
              {{ saving ? '保存中…' : '保存' }}
            </button>
          </div>
        </form>
      </div>
    </div>

    <!-- 停用确认 -->
    <div
      v-if="pendingDeactivate"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/50 p-4"
      @click.self="cancelDeactivate"
    >
      <div class="w-full max-w-md rounded-2xl bg-white p-6 shadow-xl">
        <h2 class="text-base font-semibold">停用供应商</h2>
        <p class="mt-3 text-sm leading-6 text-slate-600">
          确定要停用
          <span class="font-mono text-xs font-semibold text-slate-800">{{ pendingDeactivate.code }}</span>
          「{{ pendingDeactivate.name }}」吗？
          停用后该供应商不再出现在默认列表中，也不会被物理删除。
        </p>
        <div v-if="deactivateError" class="mt-3 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {{ deactivateError }}
        </div>
        <div class="mt-6 flex justify-end gap-3">
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
            :disabled="deactivating"
            @click="cancelDeactivate"
          >
            取消
          </button>
          <button
            type="button"
            class="rounded-lg bg-red-500 px-4 py-2 text-sm font-medium text-white transition hover:bg-red-600 disabled:cursor-not-allowed disabled:opacity-60"
            :disabled="deactivating"
            @click="confirmDeactivate"
          >
            {{ deactivating ? '停用中…' : '确认停用' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
