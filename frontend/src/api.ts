const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

let csrfToken = ''

export class ApiError extends Error {
  public readonly errors: Record<string, string[]>
  public readonly status: number
  public readonly data: Record<string, unknown>
  constructor(message: string, errors: Record<string, string[]> = {}, status = 0, data: Record<string, unknown> = {}) { super(message); this.errors = errors; this.status = status; this.data = data }
  static async from(response: Response) {
    try {
      const body = await response.json() as { detail?: string, error?: string, errors?: Record<string, string[]> }
      return new ApiError(body.detail ?? body.error ?? '', body.errors ?? {}, response.status, body as Record<string, unknown>)
    } catch { return new ApiError('', {}, response.status) }
  }
}

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

  return fetch(`${apiBaseUrl}${path}`, { ...init, credentials: 'include', cache: 'no-store', headers })
}
