const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'https://localhost:7226'

let csrfToken = ''

export async function ensureCsrfToken() {
  if (csrfToken) return csrfToken

  const response = await request('/api/auth/csrf')
  if (!response.ok) throw new Error('Could not obtain a CSRF token.')
  const payload = await response.json() as { token: string }
  csrfToken = payload.token
  return csrfToken
}

export function clearCsrfToken() {
  csrfToken = ''
}

export async function request(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')

  if (init.method && !['GET', 'HEAD', 'OPTIONS'].includes(init.method.toUpperCase())) {
    headers.set('X-CSRF-TOKEN', await ensureCsrfToken())
  }

  return fetch(`${apiBaseUrl}${path}`, { ...init, credentials: 'include', headers })
}
