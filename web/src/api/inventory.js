import http from './http'

/**
 * 库存模块 API。所有端点统一挂在 /api/v1/inventory 下：
 * - GET  /balances  库存余额分页查询（仓库 / 物料过滤 + 物料关键字）
 * - POST /inbound   入库登记
 * - POST /outbound  出库登记
 * - GET  /warnings  库存预警列表（现存量低于预警下限的仓库 × 物料组合）
 */

export async function fetchInventoryBalances({
  page = 1,
  pageSize = 10,
  warehouseId = '',
  materialId = '',
  keyword = ''
} = {}) {
  const params = { page, pageSize }
  if (warehouseId) params.warehouseId = warehouseId
  if (materialId) params.materialId = materialId
  if (keyword) params.keyword = keyword
  const { data } = await http.get('/api/v1/inventory/balances', { params })
  return data
}

export async function createInventoryInbound(payload) {
  const { data } = await http.post('/api/v1/inventory/inbound', payload)
  return data
}

export async function createInventoryOutbound(payload) {
  const { data } = await http.post('/api/v1/inventory/outbound', payload)
  return data
}

export async function fetchInventoryWarnings({
  page = 1,
  pageSize = 10,
  warehouseId = ''
} = {}) {
  const params = { page, pageSize }
  if (warehouseId) params.warehouseId = warehouseId
  const { data } = await http.get('/api/v1/inventory/warnings', { params })
  return data
}
