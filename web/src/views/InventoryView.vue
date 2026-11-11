<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import {
  createInventoryInbound,
  createInventoryOutbound,
  fetchInventoryBalances,
  fetchInventoryWarnings
} from '../api/inventory'
import { fetchCustomers } from '../api/customers'
import { fetchMaterials } from '../api/materials'
import { fetchSuppliers } from '../api/suppliers'
import { fetchWarehouses } from '../api/warehouses'

const PAGE_SIZE_OPTIONS = [10, 20, 50]
// 与后端 decimal(19,4) 保持一致：15 位整数 + 4 位小数。
const MAX_AMOUNT = 999999999999999.9999
const FIELD_LIMITS = {
  documentNo: 64,
  remark: 512
}

function formatAmount(value) {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '—'
  return Number(value).toLocaleString('zh-CN', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4
  })
}

// 金额类字段允许的最大小数位数，与后端 decimal(19,4) 一致。
function hasTooManyDecimals(value) {
  if (!Number.isFinite(value)) return false
  if (Math.abs(value) >= 1e11) return false // 超大数值交给后端校验
  return Math.round(value * 10000) / 10000 !== value
}

function nowLocalInput() {
  const now = new Date()
  const pad = (value) => String(value).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`
}

// ---------- 下拉选项（仓库 / 物料 / 供应商 / 客户） ----------
const warehouseOptions = ref([])
const materialOptions = ref([])
const supplierOptions = ref([])
const customerOptions = ref([])
const optionsLoading = ref(false)
const optionsError = ref('')

async function loadOptions() {
  optionsLoading.value = true
  optionsError.value = ''
  try {
    const [warehouses, materials, suppliers, customers] = await Promise.all([
      fetchWarehouses({ page: 1, pageSize: 100 }),
      fetchMaterials({ page: 1, pageSize: 100 }),
      fetchSuppliers({ page: 1, pageSize: 100 }),
      fetchCustomers({ page: 1, pageSize: 100 })
    ])
    warehouseOptions.value = warehouses.items ?? []
    materialOptions.value = materials.items ?? []
    supplierOptions.value = (suppliers.items ?? []).filter((row) => row.isActive)
    customerOptions.value = (customers.items ?? []).filter((row) => row.isActive)
  } catch (error) {
    optionsError.value = error.message || '加载仓库 / 物料选项失败，表单暂时无法选择。'
  } finally {
    optionsLoading.value = false
  }
}

// ---------- 区域一：库存余额查询 / 分页 ----------
const balanceItems = ref([])
const balancePage = ref(1)
const balancePageSize = ref(10)
const balanceTotalCount = ref(0)
const balanceLoading = ref(false)
const balanceError = ref('')
const balanceKeywordInput = ref('')
const balanceKeyword = ref('')
const balanceWarehouseFilter = ref('')
const balanceMaterialFilter = ref('')

const balanceTotalPages = computed(() =>
  balancePageSize.value > 0
    ? Math.max(1, Math.ceil(balanceTotalCount.value / balancePageSize.value))
    : 1
)

const balanceIsEmpty = computed(
  () => !balanceLoading.value && !balanceError.value && balanceItems.value.length === 0
)

async function loadBalances() {
  balanceLoading.value = true
  balanceError.value = ''
  try {
    const result = await fetchInventoryBalances({
      page: balancePage.value,
      pageSize: balancePageSize.value,
      warehouseId: balanceWarehouseFilter.value,
      materialId: balanceMaterialFilter.value,
      keyword: balanceKeyword.value
    })
    balanceItems.value = result.items ?? []
    balanceTotalCount.value = result.totalCount ?? 0
    // 过滤变化后当前页可能越界，回退到最后一页重新加载。
    if (balanceItems.value.length === 0 && result.page > 1 && result.page > (result.totalPages ?? 0)) {
      balancePage.value = Math.max(1, result.totalPages ?? 1)
      await loadBalances()
    }
  } catch (error) {
    balanceError.value = error.message || '加载库存余额失败'
    balanceItems.value = []
    balanceTotalCount.value = 0
  } finally {
    balanceLoading.value = false
  }
}

function searchBalances() {
  balanceKeyword.value = balanceKeywordInput.value.trim()
  balancePage.value = 1
  loadBalances()
}

function resetBalanceFilters() {
  balanceKeywordInput.value = ''
  balanceKeyword.value = ''
  balanceWarehouseFilter.value = ''
  balanceMaterialFilter.value = ''
  balancePage.value = 1
  loadBalances()
}

function changeBalancePage(next) {
  if (balanceLoading.value || next < 1 || next > balanceTotalPages.value) return
  balancePage.value = next
  loadBalances()
}

function changeBalancePageSize() {
  balancePage.value = 1
  loadBalances()
}

// ---------- 区域二 / 三：入库 / 出库表单 ----------
function createMovementForm() {
  return {
    warehouseId: '',
    materialId: '',
    documentNo: '',
    quantity: '',
    counterpartId: '',
    occurredAt: '',
    remark: ''
  }
}

function clearFieldErrors(errors) {
  for (const key of Object.keys(errors)) {
    delete errors[key]
  }
}

const inboundForm = reactive(createMovementForm())
const inboundErrors = reactive({})
const inboundSaving = ref(false)
const inboundError = ref('')
const inboundSuccess = ref('')

const outboundForm = reactive(createMovementForm())
const outboundErrors = reactive({})
const outboundSaving = ref(false)
const outboundError = ref('')
const outboundSuccess = ref('')

/**
 * 表单级校验：通过后返回可直接提交的 payload，否则返回 null。
 * 后端 decimal(19,4) / 单据号 64 位 / 备注 512 位约束在前端先行提示。
 */
function validateMovement(form, errors, kind) {
  const label = kind === 'inbound' ? '入库' : '出库'

  if (!form.warehouseId) {
    errors.warehouseId = '请选择仓库。'
  }

  if (!form.materialId) {
    errors.materialId = '请选择物料。'
  }

  const documentNo = form.documentNo.trim()
  if (!documentNo) {
    errors.documentNo = `${label}单据号必填。`
  } else if (documentNo.length > FIELD_LIMITS.documentNo) {
    errors.documentNo = `单据号长度不能超过 ${FIELD_LIMITS.documentNo} 个字符。`
  }

  let quantity = null
  if (form.quantity === '' || form.quantity === null || form.quantity === undefined) {
    errors.quantity = `${label}数量必填。`
  } else {
    const value = Number(form.quantity)
    if (!Number.isFinite(value)) {
      errors.quantity = '数量必须是有效数字。'
    } else if (value <= 0) {
      errors.quantity = '数量必须大于 0。'
    } else if (value > MAX_AMOUNT) {
      errors.quantity = `数量不能超过 ${MAX_AMOUNT}。`
    } else if (hasTooManyDecimals(value)) {
      errors.quantity = '数量最多 4 位小数。'
    } else {
      quantity = value
    }
  }

  if (form.occurredAt) {
    const occurredAt = new Date(form.occurredAt)
    if (Number.isNaN(occurredAt.getTime())) {
      errors.occurredAt = '发生时间格式不正确。'
    }
  }

  if (form.remark.trim().length > FIELD_LIMITS.remark) {
    errors.remark = `备注长度不能超过 ${FIELD_LIMITS.remark} 个字符。`
  }

  if (Object.keys(errors).length > 0) return null

  const payload = {
    documentNo,
    warehouseId: form.warehouseId,
    materialId: form.materialId,
    quantity,
    occurredAtUtc: form.occurredAt ? new Date(form.occurredAt).toISOString() : null,
    remark: form.remark.trim() || null
  }
  // 入库关联供应商（采购入库 / 客户退货），出库关联客户（销售出库 / 退供应商）。
  if (form.counterpartId) {
    if (kind === 'inbound') payload.supplierId = form.counterpartId
    else payload.customerId = form.counterpartId
  }
  return { payload }
}

// 优先展示后端 ValidationProblem 的字段级错误（400），
// 其余（404 未实现 / 409 单据号重复等）直接展示 detail / title。
// 字段名做小写归一，兼容 "$.documentNo" 与 "DocumentNo" 两种风格。
function applyServerErrors(error, form, errors, banner, fallbackLabel) {
  const problem = error.response?.data
  const problemErrors = problem?.errors
  if (problemErrors && typeof problemErrors === 'object') {
    const fieldIndex = {}
    for (const key of Object.keys(form)) {
      fieldIndex[key.toLowerCase()] = key
    }
    for (const [field, messages] of Object.entries(problemErrors)) {
      const message = Array.isArray(messages) ? messages[0] : String(messages)
      const normalized = String(field).replace(/^\$\./, '').split('.')[0].toLowerCase()
      const target = fieldIndex[normalized]
      if (target) {
        errors[target] = errors[target] || message
      } else {
        banner.value = banner.value || message
      }
    }
    if (!banner.value && Object.keys(problemErrors).length > 0) {
      banner.value = '请修正表单中的错误后重试。'
    }
    return
  }
  banner.value = error.message || `${fallbackLabel}失败，请稍后重试。`
}

function resetMovementForm(form) {
  Object.assign(form, createMovementForm(), { occurredAt: nowLocalInput() })
}

async function submitInbound() {
  if (inboundSaving.value) return
  clearFieldErrors(inboundErrors)
  inboundError.value = ''
  inboundSuccess.value = ''
  const validation = validateMovement(inboundForm, inboundErrors, 'inbound')
  if (!validation) return

  inboundSaving.value = true
  try {
    await createInventoryInbound(validation.payload)
    resetMovementForm(inboundForm)
    inboundSuccess.value = `入库单 ${validation.payload.documentNo} 登记成功，库存余额已刷新。`
    await Promise.all([loadBalances(), loadWarnings()])
  } catch (error) {
    applyServerErrors(error, inboundForm, inboundErrors, inboundError, '入库')
  } finally {
    inboundSaving.value = false
  }
}

async function submitOutbound() {
  if (outboundSaving.value) return
  clearFieldErrors(outboundErrors)
  outboundError.value = ''
  outboundSuccess.value = ''
  const validation = validateMovement(outboundForm, outboundErrors, 'outbound')
  if (!validation) return

  outboundSaving.value = true
  try {
    await createInventoryOutbound(validation.payload)
    resetMovementForm(outboundForm)
    outboundSuccess.value = `出库单 ${validation.payload.documentNo} 登记成功，库存余额已刷新。`
    await Promise.all([loadBalances(), loadWarnings()])
  } catch (error) {
    applyServerErrors(error, outboundForm, outboundErrors, outboundError, '出库')
  } finally {
    outboundSaving.value = false
  }
}

// ---------- 区域四：库存预警列表 ----------
const warningItems = ref([])
const warningPage = ref(1)
const warningPageSize = ref(10)
const warningTotalCount = ref(0)
const warningLoading = ref(false)
const warningError = ref('')
const warningWarehouseFilter = ref('')

const warningTotalPages = computed(() =>
  warningPageSize.value > 0
    ? Math.max(1, Math.ceil(warningTotalCount.value / warningPageSize.value))
    : 1
)

const warningIsEmpty = computed(
  () => !warningLoading.value && !warningError.value && warningItems.value.length === 0
)

async function loadWarnings() {
  warningLoading.value = true
  warningError.value = ''
  try {
    const result = await fetchInventoryWarnings({
      page: warningPage.value,
      pageSize: warningPageSize.value,
      warehouseId: warningWarehouseFilter.value
    })
    warningItems.value = result.items ?? []
    warningTotalCount.value = result.totalCount ?? 0
    if (warningItems.value.length === 0 && result.page > 1 && result.page > (result.totalPages ?? 0)) {
      warningPage.value = Math.max(1, result.totalPages ?? 1)
      await loadWarnings()
    }
  } catch (error) {
    warningError.value = error.message || '加载库存预警失败'
    warningItems.value = []
    warningTotalCount.value = 0
  } finally {
    warningLoading.value = false
  }
}

function searchWarnings() {
  warningPage.value = 1
  loadWarnings()
}

function changeWarningPage(next) {
  if (warningLoading.value || next < 1 || next > warningTotalPages.value) return
  warningPage.value = next
  loadWarnings()
}

function changeWarningPageSize() {
  warningPage.value = 1
  loadWarnings()
}

function formatShortfall(row) {
  const onHand = Number(row.onHandQuantity ?? 0)
  const minimum = Number(row.minimumQuantity ?? 0)
  if (Number.isNaN(onHand) || Number.isNaN(minimum)) return '—'
  return formatAmount(Math.max(0, minimum - onHand))
}

function isCriticalWarning(row) {
  return Number(row.onHandQuantity ?? 0) <= 0
}

onMounted(() => {
  resetMovementForm(inboundForm)
  resetMovementForm(outboundForm)
  loadOptions()
  loadBalances()
  loadWarnings()
})
</script>

<template>
  <div class="space-y-5">
    <!-- 页头 -->
    <div class="rounded-2xl border border-slate-200 bg-white p-6">
      <div class="text-xs font-semibold uppercase tracking-[0.2em] text-cyan-600">inventory</div>
      <h1 class="mt-2 text-2xl font-semibold">库存管理</h1>
      <p class="mt-2 max-w-2xl text-sm leading-6 text-slate-500">
        查询各仓库的库存余额，登记入库与出库，并跟踪低于预警下限的仓库 × 物料组合。
        入库 / 出库成功后余额与预警即时刷新，单据号在同一移动类型下唯一。
      </p>
      <p v-if="optionsError" class="mt-3 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-700">
        {{ optionsError }}
        <button type="button" class="ml-2 font-medium underline" :disabled="optionsLoading" @click="loadOptions">
          重试
        </button>
      </p>
    </div>

    <!-- 区域一：库存余额查询 / 分页 -->
    <section class="rounded-2xl border border-slate-200 bg-white">
      <div class="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-5 py-4">
        <div>
          <h2 class="text-sm font-semibold">库存余额</h2>
          <p class="mt-0.5 text-xs text-slate-500">按仓库 / 物料过滤，支持物料编码与名称关键字搜索。</p>
        </div>
        <span class="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs text-slate-500">共 {{ balanceTotalCount }} 条</span>
      </div>

      <form class="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4" @submit.prevent="searchBalances">
        <input
          v-model="balanceKeywordInput"
          type="search"
          class="w-full max-w-xs rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-100"
          placeholder="按物料编码 / 名称搜索"
          :disabled="balanceLoading"
        />
        <select
          v-model="balanceWarehouseFilter"
          class="rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400"
          :disabled="balanceLoading"
          @change="searchBalances"
        >
          <option value="">全部仓库</option>
          <option v-for="row in warehouseOptions" :key="row.id" :value="row.id">
            {{ row.code }} · {{ row.name }}
          </option>
        </select>
        <select
          v-model="balanceMaterialFilter"
          class="rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400"
          :disabled="balanceLoading"
          @change="searchBalances"
        >
          <option value="">全部物料</option>
          <option v-for="row in materialOptions" :key="row.id" :value="row.id">
            {{ row.code }} · {{ row.name }}
          </option>
        </select>
        <button
          type="submit"
          class="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
          :disabled="balanceLoading"
        >
          搜索
        </button>
        <button
          type="button"
          class="rounded-lg px-3 py-2 text-sm text-slate-500 transition hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-60"
          :disabled="balanceLoading"
          @click="resetBalanceFilters"
        >
          重置
        </button>
      </form>

      <!-- 加载失败 -->
      <div v-if="balanceError" class="p-6">
        <div class="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          <span>{{ balanceError }}</span>
          <button
            type="button"
            class="rounded-lg border border-red-300 px-3 py-1.5 text-xs font-medium transition hover:bg-red-100"
            @click="loadBalances"
          >
            重试
          </button>
        </div>
      </div>

      <!-- 加载中 -->
      <div v-else-if="balanceLoading" class="flex items-center justify-center gap-3 p-12 text-sm text-slate-500">
        <span class="h-4 w-4 animate-spin rounded-full border-2 border-slate-300 border-t-cyan-500" />
        正在加载库存余额…
      </div>

      <!-- 空数据 -->
      <div v-else-if="balanceIsEmpty" class="p-6">
        <div class="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-10 text-center">
          <div class="text-sm font-medium text-slate-700">
            {{ balanceKeyword || balanceWarehouseFilter || balanceMaterialFilter ? '没有符合条件的库存余额' : '还没有库存余额' }}
          </div>
          <p class="mt-2 text-xs text-slate-500">
            {{ balanceKeyword || balanceWarehouseFilter || balanceMaterialFilter ? '调整关键字或筛选条件后重新搜索。' : '通过下方入库表单登记第一笔库存。' }}
          </p>
        </div>
      </div>

      <!-- 余额表格 -->
      <div v-else class="overflow-x-auto">
        <table class="w-full text-left text-sm">
          <thead>
            <tr class="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-500">
              <th class="px-4 py-3 font-medium">仓库编码</th>
              <th class="px-4 py-3 font-medium">仓库名称</th>
              <th class="px-4 py-3 font-medium">物料编码</th>
              <th class="px-4 py-3 font-medium">物料名称</th>
              <th class="px-4 py-3 font-medium">单位</th>
              <th class="px-4 py-3 text-right font-medium">现有数量</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in balanceItems"
              :key="row.id"
              class="border-b border-slate-50 transition last:border-0 hover:bg-slate-50/70"
            >
              <td class="whitespace-nowrap px-4 py-3 font-mono text-xs font-semibold text-slate-800">
                {{ row.warehouseCode }}
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-600">{{ row.warehouseName }}</td>
              <td class="whitespace-nowrap px-4 py-3 font-mono text-xs font-semibold text-slate-800">
                {{ row.materialCode }}
              </td>
              <td class="px-4 py-3 font-medium text-slate-800">{{ row.materialName }}</td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-600">{{ row.materialUnit || '—' }}</td>
              <td class="whitespace-nowrap px-4 py-3 text-right font-mono text-xs text-slate-600">
                {{ formatAmount(row.onHandQuantity) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 余额分页 -->
      <div
        v-if="!balanceError"
        class="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-4 py-3 text-xs text-slate-500"
      >
        <div class="flex items-center gap-2">
          <span>每页</span>
          <select
            v-model.number="balancePageSize"
            class="rounded-lg border border-slate-200 px-2 py-1 text-xs outline-none focus:border-cyan-400"
            :disabled="balanceLoading"
            @change="changeBalancePageSize"
          >
            <option v-for="option in PAGE_SIZE_OPTIONS" :key="option" :value="option">
              {{ option }}
            </option>
          </select>
          <span>条，共 {{ balanceTotalCount }} 条</span>
        </div>
        <div class="flex items-center gap-2">
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-3 py-1.5 font-medium transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            :disabled="balanceLoading || balancePage <= 1"
            @click="changeBalancePage(balancePage - 1)"
          >
            上一页
          </button>
          <span>第 {{ balancePage }} / {{ balanceTotalPages }} 页</span>
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-3 py-1.5 font-medium transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            :disabled="balanceLoading || balancePage >= balanceTotalPages"
            @click="changeBalancePage(balancePage + 1)"
          >
            下一页
          </button>
        </div>
      </div>
    </section>

    <!-- 区域二 / 三：入库 / 出库 -->
    <div class="grid gap-5 lg:grid-cols-2">
      <!-- 入库 -->
      <section class="rounded-2xl border border-slate-200 bg-white">
        <div class="border-b border-slate-100 px-5 py-4">
          <h2 class="text-sm font-semibold">入库登记</h2>
          <p class="mt-0.5 text-xs text-slate-500">采购入库 / 生产完工入库 / 客户退货入库。</p>
        </div>

        <form class="grid gap-4 px-5 py-5 sm:grid-cols-2" @submit.prevent="submitInbound">
          <div v-if="inboundError" class="sm:col-span-2">
            <div class="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
              {{ inboundError }}
            </div>
          </div>
          <div v-if="inboundSuccess" class="sm:col-span-2">
            <div class="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-700">
              {{ inboundSuccess }}
            </div>
          </div>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">仓库 *</span>
            <select
              v-model="inboundForm.warehouseId"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100 disabled:bg-slate-50"
              :class="inboundErrors.warehouseId ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :disabled="inboundSaving || optionsLoading"
            >
              <option value="">请选择仓库</option>
              <option v-for="row in warehouseOptions" :key="row.id" :value="row.id">
                {{ row.code }} · {{ row.name }}
              </option>
            </select>
            <span v-if="inboundErrors.warehouseId" class="mt-1 block text-xs text-red-600">{{ inboundErrors.warehouseId }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">物料 *</span>
            <select
              v-model="inboundForm.materialId"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100 disabled:bg-slate-50"
              :class="inboundErrors.materialId ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :disabled="inboundSaving || optionsLoading"
            >
              <option value="">请选择物料</option>
              <option v-for="row in materialOptions" :key="row.id" :value="row.id">
                {{ row.code }} · {{ row.name }}
              </option>
            </select>
            <span v-if="inboundErrors.materialId" class="mt-1 block text-xs text-red-600">{{ inboundErrors.materialId }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">入库单号 *</span>
            <input
              v-model="inboundForm.documentNo"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="inboundErrors.documentNo ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.documentNo"
              placeholder="PO-2026-0001"
              :disabled="inboundSaving"
            />
            <span v-if="inboundErrors.documentNo" class="mt-1 block text-xs text-red-600">{{ inboundErrors.documentNo }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">入库数量 *</span>
            <input
              v-model.number="inboundForm.quantity"
              type="number"
              step="0.0001"
              min="0"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="inboundErrors.quantity ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              placeholder="0.0000"
              :disabled="inboundSaving"
            />
            <span v-if="inboundErrors.quantity" class="mt-1 block text-xs text-red-600">{{ inboundErrors.quantity }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">供应商</span>
            <select
              v-model="inboundForm.counterpartId"
              class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-100 disabled:bg-slate-50"
              :disabled="inboundSaving || optionsLoading"
            >
              <option value="">不关联</option>
              <option v-for="row in supplierOptions" :key="row.id" :value="row.id">
                {{ row.code }} · {{ row.name }}
              </option>
            </select>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">发生时间</span>
            <input
              v-model="inboundForm.occurredAt"
              type="datetime-local"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="inboundErrors.occurredAt ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :disabled="inboundSaving"
            />
            <span v-if="inboundErrors.occurredAt" class="mt-1 block text-xs text-red-600">{{ inboundErrors.occurredAt }}</span>
          </label>

          <label class="block text-sm sm:col-span-2">
            <span class="mb-1 block font-medium text-slate-700">备注</span>
            <input
              v-model="inboundForm.remark"
              type="text"
              class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-100"
              :maxlength="FIELD_LIMITS.remark"
              :disabled="inboundSaving"
            />
            <span v-if="inboundErrors.remark" class="mt-1 block text-xs text-red-600">{{ inboundErrors.remark }}</span>
          </label>

          <div class="flex justify-end sm:col-span-2">
            <button
              type="submit"
              class="rounded-lg bg-cyan-500 px-4 py-2 text-sm font-medium text-white transition hover:bg-cyan-600 disabled:cursor-not-allowed disabled:opacity-60"
              :disabled="inboundSaving"
            >
              {{ inboundSaving ? '登记中…' : '登记入库' }}
            </button>
          </div>
        </form>
      </section>

      <!-- 出库 -->
      <section class="rounded-2xl border border-slate-200 bg-white">
        <div class="border-b border-slate-100 px-5 py-4">
          <h2 class="text-sm font-semibold">出库登记</h2>
          <p class="mt-0.5 text-xs text-slate-500">销售出库 / 生产领料 / 退供应商出库。</p>
        </div>

        <form class="grid gap-4 px-5 py-5 sm:grid-cols-2" @submit.prevent="submitOutbound">
          <div v-if="outboundError" class="sm:col-span-2">
            <div class="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
              {{ outboundError }}
            </div>
          </div>
          <div v-if="outboundSuccess" class="sm:col-span-2">
            <div class="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-700">
              {{ outboundSuccess }}
            </div>
          </div>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">仓库 *</span>
            <select
              v-model="outboundForm.warehouseId"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100 disabled:bg-slate-50"
              :class="outboundErrors.warehouseId ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :disabled="outboundSaving || optionsLoading"
            >
              <option value="">请选择仓库</option>
              <option v-for="row in warehouseOptions" :key="row.id" :value="row.id">
                {{ row.code }} · {{ row.name }}
              </option>
            </select>
            <span v-if="outboundErrors.warehouseId" class="mt-1 block text-xs text-red-600">{{ outboundErrors.warehouseId }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">物料 *</span>
            <select
              v-model="outboundForm.materialId"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100 disabled:bg-slate-50"
              :class="outboundErrors.materialId ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :disabled="outboundSaving || optionsLoading"
            >
              <option value="">请选择物料</option>
              <option v-for="row in materialOptions" :key="row.id" :value="row.id">
                {{ row.code }} · {{ row.name }}
              </option>
            </select>
            <span v-if="outboundErrors.materialId" class="mt-1 block text-xs text-red-600">{{ outboundErrors.materialId }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">出库单号 *</span>
            <input
              v-model="outboundForm.documentNo"
              type="text"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="outboundErrors.documentNo ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :maxlength="FIELD_LIMITS.documentNo"
              placeholder="SO-2026-0001"
              :disabled="outboundSaving"
            />
            <span v-if="outboundErrors.documentNo" class="mt-1 block text-xs text-red-600">{{ outboundErrors.documentNo }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">出库数量 *</span>
            <input
              v-model.number="outboundForm.quantity"
              type="number"
              step="0.0001"
              min="0"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="outboundErrors.quantity ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              placeholder="0.0000"
              :disabled="outboundSaving"
            />
            <span v-if="outboundErrors.quantity" class="mt-1 block text-xs text-red-600">{{ outboundErrors.quantity }}</span>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">客户</span>
            <select
              v-model="outboundForm.counterpartId"
              class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-100 disabled:bg-slate-50"
              :disabled="outboundSaving || optionsLoading"
            >
              <option value="">不关联</option>
              <option v-for="row in customerOptions" :key="row.id" :value="row.id">
                {{ row.code }} · {{ row.name }}
              </option>
            </select>
          </label>

          <label class="block text-sm">
            <span class="mb-1 block font-medium text-slate-700">发生时间</span>
            <input
              v-model="outboundForm.occurredAt"
              type="datetime-local"
              class="w-full rounded-lg border px-3 py-2 text-sm outline-none transition focus:ring-2 focus:ring-cyan-100"
              :class="outboundErrors.occurredAt ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-cyan-400'"
              :disabled="outboundSaving"
            />
            <span v-if="outboundErrors.occurredAt" class="mt-1 block text-xs text-red-600">{{ outboundErrors.occurredAt }}</span>
          </label>

          <label class="block text-sm sm:col-span-2">
            <span class="mb-1 block font-medium text-slate-700">备注</span>
            <input
              v-model="outboundForm.remark"
              type="text"
              class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-100"
              :maxlength="FIELD_LIMITS.remark"
              :disabled="outboundSaving"
            />
            <span v-if="outboundErrors.remark" class="mt-1 block text-xs text-red-600">{{ outboundErrors.remark }}</span>
          </label>

          <div class="flex justify-end sm:col-span-2">
            <button
              type="submit"
              class="rounded-lg bg-cyan-500 px-4 py-2 text-sm font-medium text-white transition hover:bg-cyan-600 disabled:cursor-not-allowed disabled:opacity-60"
              :disabled="outboundSaving"
            >
              {{ outboundSaving ? '登记中…' : '登记出库' }}
            </button>
          </div>
        </form>
      </section>
    </div>

    <!-- 区域四：库存预警列表 -->
    <section class="rounded-2xl border border-slate-200 bg-white">
      <div class="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-4">
        <div>
          <h2 class="text-sm font-semibold">库存预警</h2>
          <p class="mt-0.5 text-xs text-slate-500">现存量低于预警下限的仓库 × 物料组合，需及时补货。</p>
        </div>
        <label class="flex items-center gap-2 text-xs text-slate-500">
          仓库
          <select
            v-model="warningWarehouseFilter"
            class="rounded-lg border border-slate-200 px-3 py-1.5 text-xs outline-none transition focus:border-cyan-400"
            :disabled="warningLoading"
            @change="searchWarnings"
          >
            <option value="">全部仓库</option>
            <option v-for="row in warehouseOptions" :key="row.id" :value="row.id">
              {{ row.code }} · {{ row.name }}
            </option>
          </select>
        </label>
      </div>

      <!-- 加载失败 -->
      <div v-if="warningError" class="p-6">
        <div class="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          <span>{{ warningError }}</span>
          <button
            type="button"
            class="rounded-lg border border-red-300 px-3 py-1.5 text-xs font-medium transition hover:bg-red-100"
            @click="loadWarnings"
          >
            重试
          </button>
        </div>
      </div>

      <!-- 加载中 -->
      <div v-else-if="warningLoading" class="flex items-center justify-center gap-3 p-12 text-sm text-slate-500">
        <span class="h-4 w-4 animate-spin rounded-full border-2 border-slate-300 border-t-cyan-500" />
        正在加载库存预警…
      </div>

      <!-- 空数据 -->
      <div v-else-if="warningIsEmpty" class="p-6">
        <div class="rounded-lg border border-dashed border-emerald-200 bg-emerald-50/60 p-10 text-center">
          <div class="text-sm font-medium text-emerald-700">暂无库存预警</div>
          <p class="mt-2 text-xs text-emerald-600/80">所有启用规则的仓库 × 物料组合都在预警下限之上。</p>
        </div>
      </div>

      <!-- 预警表格 -->
      <div v-else class="overflow-x-auto">
        <table class="w-full text-left text-sm">
          <thead>
            <tr class="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-500">
              <th class="px-4 py-3 font-medium">仓库</th>
              <th class="px-4 py-3 font-medium">物料</th>
              <th class="px-4 py-3 font-medium">单位</th>
              <th class="px-4 py-3 text-right font-medium">现有数量</th>
              <th class="px-4 py-3 text-right font-medium">预警下限</th>
              <th class="px-4 py-3 text-right font-medium">缺口</th>
              <th class="px-4 py-3 font-medium">状态</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in warningItems"
              :key="row.id"
              class="border-b border-slate-50 transition last:border-0 hover:bg-slate-50/70"
            >
              <td class="whitespace-nowrap px-4 py-3">
                <span class="font-mono text-xs font-semibold text-slate-800">{{ row.warehouseCode }}</span>
                <span class="ml-2 text-slate-600">{{ row.warehouseName }}</span>
              </td>
              <td class="whitespace-nowrap px-4 py-3">
                <span class="font-mono text-xs font-semibold text-slate-800">{{ row.materialCode }}</span>
                <span class="ml-2 text-slate-600">{{ row.materialName }}</span>
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-600">{{ row.materialUnit || '—' }}</td>
              <td class="whitespace-nowrap px-4 py-3 text-right font-mono text-xs text-slate-600">
                {{ formatAmount(row.onHandQuantity) }}
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-right font-mono text-xs text-slate-600">
                {{ formatAmount(row.minimumQuantity) }}
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-right font-mono text-xs font-semibold text-red-600">
                {{ formatShortfall(row) }}
              </td>
              <td class="whitespace-nowrap px-4 py-3">
                <span
                  class="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium"
                  :class="isCriticalWarning(row) ? 'bg-red-50 text-red-700' : 'bg-amber-50 text-amber-700'"
                >
                  <span class="h-1.5 w-1.5 rounded-full" :class="isCriticalWarning(row) ? 'bg-red-500' : 'bg-amber-500'" />
                  {{ isCriticalWarning(row) ? '缺料' : '低于下限' }}
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 预警分页 -->
      <div
        v-if="!warningError"
        class="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-4 py-3 text-xs text-slate-500"
      >
        <div class="flex items-center gap-2">
          <span>每页</span>
          <select
            v-model.number="warningPageSize"
            class="rounded-lg border border-slate-200 px-2 py-1 text-xs outline-none focus:border-cyan-400"
            :disabled="warningLoading"
            @change="changeWarningPageSize"
          >
            <option v-for="option in PAGE_SIZE_OPTIONS" :key="option" :value="option">
              {{ option }}
            </option>
          </select>
          <span>条，共 {{ warningTotalCount }} 条</span>
        </div>
        <div class="flex items-center gap-2">
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-3 py-1.5 font-medium transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            :disabled="warningLoading || warningPage <= 1"
            @click="changeWarningPage(warningPage - 1)"
          >
            上一页
          </button>
          <span>第 {{ warningPage }} / {{ warningTotalPages }} 页</span>
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-3 py-1.5 font-medium transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            :disabled="warningLoading || warningPage >= warningTotalPages"
            @click="changeWarningPage(warningPage + 1)"
          >
            下一页
          </button>
        </div>
      </div>
    </section>
  </div>
</template>
