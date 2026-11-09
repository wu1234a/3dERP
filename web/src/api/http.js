import axios from 'axios'

const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '',
  timeout: 20000
})

http.interceptors.response.use(
  (response) => response,
  (error) => {
    const problem = error.response?.data
    const message = problem?.detail || problem?.title || error.message || '请求失败'
    return Promise.reject(Object.assign(error, { message }))
  }
)

export default http
