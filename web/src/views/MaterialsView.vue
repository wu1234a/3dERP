<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import {
  createMaterial,
  deactivateMaterial,
  fetchMaterials,
  updateMaterial
} from '../api/materials'

const route = useRoute()

const PAGE_SIZE_OPTIONS = [10, 20, 50]
const CODE_PATTERN = /^[A-Z0-9-]+$/
const FIELD_LIMITS = {
  code: 50,
  name: 100,
  specification: 200,
  unit: 20,
  materialType: 50
}
// 与后端 decimal(19,4) 保持一致：15 位整数 + 4 位小数。
const MAX_AMOUNT = 999999999999999.9999

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

async function loadMaterials() {
  loading.value = true
  loadError.value = ''
  try {
    const result = await fetchMaterials({
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
      await loadMaterials()
    }
  } catch (error) {
    loadError.value = error.message || '加载物料列表失败'
    items.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

function search() {
  keyword.value = keywordInput.value.trim()
  page.value = 1
  loadMaterials()
}

function resetSearch() {
  keywordInput.value = ''
  keyword.value = ''
  includeInactive.value = false
  page.value = 1
  loadMaterials()
}

function toggleIncludeInactive() {
  page.value = 1
  loadMaterials()
}

function changePage(next) {
  if (loading.value || next < 1 || next > totalPages.value) return
  page.value = next
  loadMaterials()
}

function changePageSize() {
  page.value = 1
  loadMaterials()
}

function formatDateTime(value) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? '—'
    : date.toLocaleString('zh-CN', { hour12: false })
}

function formatAmount(value) {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '—'
  return Number(value).toLocaleString('zh-CN', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4
  })
}

// ---------- 新建 / 编辑对话框 ----------
const dialogOpen = ref(false)
const saving = ref(false)
const dialogError = ref('')
const editingId = ref(null)

const form = reactive({
  code: '',
  name: '',
  specification: '',
  unit: '',
  materialType: '',
  unitPrice: '',
  safetyStock: ''
})

const formErrors = reactive({})

const dialogTitle = computed(() => (editingId.value ? '编辑物料' : '新建物料'))

function resetForm() {
  Object.assign(form, {
    code: '',
    name: '',
    specification: '',
    unit: '',
    materialType: '',
    unitPrice: '',
    safetyStock: ''
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
    specification: row.specification ?? '',
    unit: row.unit ?? '',
    materialType: row.materialType ?? '',
    unitPrice: row.unitPrice ?? '',
    safetyStock: row.safetyStock ?? ''
  })
  clearFormErrors()
  dialogOpen.value = true
}

function closeDialog() {
  if (saving.value) return
  dialogOpen.value = false
}

// 金额类字段允许的最大小数位数，与后端 decimal(19,4) 一致。
function hasTooManyDecimals(value) {
  if (!Number.isFinite(value)) return false
  if (Math.abs(value) >= 1e11) return false // 超大数值交给后端校验
  return Math.round(value * 10000) / 10000 !== value
}

function validateAmount(field, label, raw) {
  if (raw === '' || raw === null || raw === undefined) {
    formErrors[field] = `${label}必填。`
    return null
  }
  const value = Number(raw)
  if (!Number.isFinite(value)) {
    formErrors[field] = `${label}必须是有效数字。`
    return null
  }
  if (value < 0) {
    formErrors[field] = `${label}不能为负数。`
    return null
  }
  if (value > MAX_AMOUNT) {
    formErrors[field] = `${label}不能超过 ${MAX_AMOUNT}。`
    return null
  }
  if (hasTooManyDecimals(value)) {
    formErrors[field] = `${label}最多 4 位小数。`
    return null
  }
  return value
}

function validateForm() {
  clearFormErrors()

  const code = form.code.trim()
  if (!code) {
    formErrors.code = '物料编码必填。'
  } else if (code.length > FIELD_LIMITS.code) {
    formErrors.code = `物料编码长度不能超过 ${FIELD_LIMITS.code} 个字符。`
  } else if (!CODE_PATTERN.test(code)) {
    formErrors.code = '物料编码只能包含大写字母、数字和短横线。'
  }

  const name = form.name.trim()
  if (!name) {
    formErrors.name = '物料名称必填。'
  } else if (name.length > FIELD_LIMITS.name) {
    formErrors.name = `物料名称长度不能超过 ${FIELD_LIMITS.name} 个字符。`
  }

  if (form.specification.trim().length > FIELD_LIMITS.specification) {
    formErrors.specification = `规格长度不能超过 ${FIELD_LIMITS.specification} 个字符。`
  }

  const unit = form.unit.trim()
  if (!unit) {
    formErrors.unit = '计量单位必填。'
  } else if (unit.length > FIELD_LIMITS.unit) {
    formErrors.unit = `计量单位长度不能超过 ${FIELD_LIMITS.unit} 个字符。`
  }

  const materialType = form.materialType.trim()
  if (!materialType) {
    formErrors.materialType = '物料类型必填。'
  } else if (materialType.length > FIELD_LIMITS.materialType) {
    formErrors.materialType = `物料类型长度不能超过 ${FIELD_LIMITS.materialType} 个字符。`
  }

  const unitPrice = validateAmount('unitPrice', '单价', form.unitPrice)
  const safetyStock = validateAmount('safetyStock', '安全库存', form.safetyStock)

  return {
    valid: Object.keys(formErrors).length === 0,
    unitPrice,
    safetyStock
  }
}

async function submitForm() {
  if (saving.value) return
  const validation = validateForm()
  if (!validation.valid) return

  saving.value = true
  try {
    const payload = {
      code: form.code.trim(),
      name: form.name.trim(),
      specification: form.specification.trim() || null,
      unit: form.unit.trim(),
      materialType: form.materialType.trim(),
      unitPrice: validation.unitPrice,
      safetyStock: validation.safetyStock
    }
    if (editingId.value) {
      await updateMaterial(editingId.value, payload)
    } else {
      await createMaterial(payload)
    }
    dialogOpen.value = false
    page.value = 1
    await loadMaterials()
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
    await deactivateMaterial(pendingDeactivate.value.id)
    pendingDeactivate.value = null
    await loadMaterials()
  } catch (error) {
    deactivateError.value = error.message || '停用失败，请稍后重试。'
  } finally {
    deactivating.value = false
  }
}

onMounted(loadMaterials)
</script>

<template>
  <div class="space-y-5">
    <!-- 页头 -->
    <div class="rounded-2xl border border-slate-200 bg-white p-6">
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div class="text-xs font-semibold uppercase tracking-[0.2em] text-cyan-600">master data</div>
          <h1 class="mt-2 text-2xl font-semibold">物料主数据</h1>
          <p class="mt-2 max-w-2xl text-sm leading-6 text-slate-500">
            树脂、金属粉末、线材等 3D 打印物料。物料编码全局唯一，单价与安全库存使用 decimal(19,4)。
            停用为软删除，不物理删除数据。
          </p>
        </div>
        <button
          type="button"
          class="rounded-lg bg-cyan-500 px-4 py-2 text-sm font-medium text-white transition hover:bg-cyan-600 disabled:cursor-not-allowed disabled:opacity-60"
          :disabled="loading"
          @click="openCreateDialog"
        >
          新建物料
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
          placeholder="按编码 / 名称 / 物料类型搜索"
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
            @click="loadMaterials"
          >
            重试
          </button>
        </div>
      </div>

      <!-- 加载中 -->
      <div v-else-if="loading" class="flex items-center justify-center gap-3 p-12 text-sm text-slate-500">
        <span class="h-4 w-4 animate-spin rounded-full border-2 border-slate-300 border-t-cyan-500" />
        正在加载物料数据…
      </div>

      <!-- 空数据 -->
      <div v-else-if="isEmpty" class="p-6">
        <div class="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-10 text-center">
          <div class="text-sm font-medium text-slate-700">
            {{ keyword || includeInactive ? '没有符合条件的物料' : '还没有物料' }}
          </div>
          <p class="mt-2 text-xs text-slate-500">
            {{ keyword || includeInactive ? '调整关键字或筛选条件后重新搜索。' : '点击右上角「新建物料」创建第一个物料。' }}
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
              <th class="px-4 py-3 font-medium">类型</th>
              <th class="px-4 py-3 font-medium">规格</th>
              <th class="px-4 py-3 font-medium">单位</th>
              <th class="px-4 py-3 font-medium">单价</th>
              <th class="px-4 py-3 font-medium">安全库存</th>
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
              <td class="whitespace-nowrap px-4 py-3 text-slate-600">{{ row.materialType || '—' }}</td>
              <td class="max-w-[16rem] truncate px-4 py-3 text-slate-600" :title="row.specification || ''">
                {{ row.specification || '—' }}
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-600">{{ row.unit || '—' }}</td>
              <td class="whitespace-nowrap px-4 py-3 text-right font-mono text-xs text-slate-600">
                {{ formatAmount(row.unitPrice) }}
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-right font-mono text-xs text-slate-600">
                {{ formatAmount(row.safetyStock) }}
              </td>
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
            <p class="mt-0.5 text-xs text-slate-500">编码仅支持大写字母、数字与短横线，创建后全局唯一；金额最多 4 位小数。</p>
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
            <span class="mb-1 block font-medium text-slate-700">物料编码 *</span>
            <input
              v-model="form.code"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.code ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.code"
              placeholder="MAT-RESIN-001"
              :disabled="saving"
            />
            <span v-if="formErrors.code" class="mt-1 block text-xs text-red-600">{{ formErrors.code }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">物料名称 *</span>
            <input
              v-model="form.name"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.name ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.name"
              placeholder="光敏树脂"
              :disabled="saving"
            />
            <span v-if="formErrors.name" class="mt-1 block text-xs text-red-600">{{ formErrors.name }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">物料类型 *</span>
            <input
              v-model="form.materialType"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.materialType ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.materialType"
              placeholder="树脂 / 金属粉末 / 线材"
              :disabled="saving"
            />
            <span v-if="formErrors.materialType" class="mt-1 block text-xs text-red-600">{{ formErrors.materialType }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">计量单位 *</span>
            <input
              v-model="form.unit"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.unit ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.unit"
              placeholder="kg / L / 个"
              :disabled="saving"
            />
            <span v-if="formErrors.unit" class="mt-1 block text-xs text-red-600">{{ formErrors.unit }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">单价 *</span>
            <input
              v-model.number="form.unitPrice"
              type="number"
              step="0.0001"
              min="0"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.unitPrice ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              placeholder="0.0000"
              :disabled="saving"
            />
            <span v-if="formErrors.unitPrice" class="mt-1 block text-xs text-red-600">{{ formErrors.unitPrice }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">安全库存 *</span>
            <input
              v-model.number="form.safetyStock"
              type="number"
              step="0.0001"
              min="0"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.safetyStock ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              placeholder="0.0000"
              :disabled="saving"
            />
            <span v-if="formErrors.safetyStock" class="mt-1 block text-xs text-red-600">{{ formErrors.safetyStock }}</span>
          </label>

          <label class="block text-sm sm:col-span-2">
            <span class="mb-1 block font-medium text-slate-700">规格</span>
            <input
              v-model="form.specification"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="formErrors.specification ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.specification"
              placeholder="SLA 通用树脂 1kg 装"
              :disabled="saving"
            />
            <span v-if="formErrors.specification" class="mt-1 block text-xs text-red-600">{{ formErrors.specification }}</span>
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
        <h2 class="text-base font-semibold">停用物料</h2>
        <p class="mt-3 text-sm leading-6 text-slate-600">
          确定要停用
          <span class="font-mono text-xs font-semibold text-slate-800">{{ pendingDeactivate.code }}</span>
          「{{ pendingDeactivate.name }}」吗？
          停用后该物料不再出现在默认列表中，也不会被物理删除。
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
