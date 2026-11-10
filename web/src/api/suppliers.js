import http from './http'

/**
 * 供应商主数据 API。与后端 /api/v1/suppliers 端点一一对应，
 * DELETE 为停用（软删除），不做物理删除。
 */

export async function fetchSuppliers({
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
  const { data } = await http.get('/api/v1/suppliers', { params })
  return data
}

export async function fetchSupplier(id) {
  const { data } = await http.get(`/api/v1/suppliers/${id}`)
  return data
}

export async function createSupplier(payload) {
  const { data } = await http.post('/api/v1/suppliers', payload)
  return data
}

export async function updateSupplier(id, payload) {
  const { data } = await http.put(`/api/v1/suppliers/${id}`, payload)
  return data
}

export async function deactivateSupplier(id) {
  const { data } = await http.delete(`/api/v1/suppliers/${id}`)
  return data
}
