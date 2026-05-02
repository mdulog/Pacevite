import axios from 'axios'

// JWT stored in memory — never localStorage/sessionStorage (XSS resistance, ADR 0004)
let accessToken: string | null = null

export const tokenStore = {
  set: (token: string) => { accessToken = token },
  clear: () => { accessToken = null },
  get: () => accessToken,
}

// Registered by AuthProvider on mount so the interceptor can clear auth state
// without a circular import to AuthContext.
let onLogoutCallback: (() => void) | null = null

export function setLogoutCallback(cb: () => void) {
  onLogoutCallback = cb
}

export const apiClient = axios.create({ baseURL: '/api' })

apiClient.interceptors.request.use(config => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  return config
})

let isRefreshing = false
let refreshQueue: Array<{
  resolve: (token: string) => void
  reject: (err: unknown) => void
}> = []

apiClient.interceptors.response.use(
  response => response,
  async error => {
    const originalRequest = error.config

    // Not a 401, or already retried after a refresh — don't attempt again
    if (error.response?.status !== 401 || originalRequest._isRetry) {
      return Promise.reject(error)
    }

    // A refresh is already in flight — queue this request to retry when it resolves
    if (isRefreshing) {
      return new Promise<string>((resolve, reject) => {
        refreshQueue.push({ resolve, reject })
      }).then(newToken => {
        originalRequest.headers.Authorization = `Bearer ${newToken}`
        return apiClient(originalRequest)
      })
    }

    originalRequest._isRetry = true
    isRefreshing = true

    try {
      const { data } = await apiClient.post<{ token: string }>('/auth/refresh')
      tokenStore.set(data.token)
      refreshQueue.forEach(({ resolve }) => resolve(data.token))
      refreshQueue = []
      originalRequest.headers.Authorization = `Bearer ${data.token}`
      return apiClient(originalRequest)
    } catch (refreshError) {
      refreshQueue.forEach(({ reject }) => reject(refreshError))
      refreshQueue = []
      onLogoutCallback?.()
      return Promise.reject(refreshError)
    } finally {
      isRefreshing = false
    }
  }
)
