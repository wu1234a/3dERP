import http from './http'

/**
 * 客户主数据 API。与后端 /api/v1/customers 端点一一对应，
 * DELETE 为停用（软删除），不做物理删除。
 */

export async function fetchCustomers({
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
  const { data } = await http.get('/api/v1/customers', { params })
  return data
}

export async function fetchCustomer(id) {
  const { data } = await http.get(`/api/v1/customers/${id}`)
  return data
}

export async function createCustomer(payload) {
  const { data } = await http.post('/api/v1/customers', payload)
  return data
}

export async function updateCustomer(id, payload) {
  const { data } = await http.put(`/api/v1/customers/${id}`, payload)
  return data
}

export async function deactivateCustomer(id) {
  const { data } = await http.delete(`/api/v1/customers/${id}`)
  return data
}
