import http from './http'

/**
 * 物料主数据 API。与后端 /api/v1/materials 端点一一对应，
 * DELETE 为停用（软删除），不做物理删除。
 */

export async function fetchMaterials({
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
  const { data } = await http.get('/api/v1/materials', { params })
  return data
}

export async function fetchMaterial(id) {
  const { data } = await http.get(`/api/v1/materials/${id}`)
  return data
}

export async function createMaterial(payload) {
  const { data } = await http.post('/api/v1/materials', payload)
  return data
}

export async function updateMaterial(id, payload) {
  const { data } = await http.put(`/api/v1/materials/${id}`, payload)
  return data
}

export async function deactivateMaterial(id) {
  const { data } = await http.delete(`/api/v1/materials/${id}`)
  return data
}
