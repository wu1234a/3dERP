import http from './http'

/**
 * 仓库主数据 API。与后端 /api/v1/warehouses 端点一一对应，
 * DELETE 为停用（软删除），不做物理删除。
 */

export async function fetchWarehouses({
  page = 1,
  pageSize = 10,
  keyword = '',
  includeInactive = false
} = {}) {
  const params = {
    page,
    pageSize,
    includeInactive
  }
  if (keyword) {
    params.keyword = keyword
  }
  const { data } = await http.get('/api/v1/warehouses', { params })
  return data
}

export async function fetchWarehouse(id) {
  const { data } = await http.get(`/api/v1/warehouses/${id}`)
  return data
}

export async function createWarehouse(payload) {
  const { data } = await http.post('/api/v1/warehouses', payload)
  return data
}

export async function updateWarehouse(id, payload) {
  const { data } = await http.put(`/api/v1/warehouses/${id}`, payload)
  return data
}

export async function deactivateWarehouse(id) {
  const { data } = await http.delete(`/api/v1/warehouses/${id}`)
  return data
}
