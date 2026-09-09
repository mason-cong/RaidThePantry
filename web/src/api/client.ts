/**
 * The one place that talks to the network.
 *
 * Everything above this file deals in typed values and ApiError, never in
 * Response objects or status codes — so error handling is written once here
 * instead of at every call site.
 */

/** Where the token lives. See the note in auth/AuthContext.tsx on the tradeoff. */
const TOKEN_KEY = 'recipefinder.token'

let authToken: string | null = readStoredToken()

/** Called when the server rejects a token, so the UI can drop to signed-out. */
let onUnauthorized: (() => void) | null = null

function readStoredToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    // Safari in private mode throws on localStorage access rather than
    // returning null. Signed-out is the correct fallback.
    return null
  }
}

export function setAuthToken(token: string | null) {
  authToken = token
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token)
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    // Storage unavailable: the token still works for this tab, it just will
    // not survive a reload. Better than failing the sign-in outright.
  }
}

export function getAuthToken(): string | null {
  return authToken
}

export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler
}

export class ApiError extends Error {
  readonly status: number
  /** Field-level validation errors from ProblemDetails, when present. */
  readonly fieldErrors?: Record<string, string[]>
  /** The parsed response body, for the rare caller that needs more than a message. */
  readonly body?: unknown

  // Fields are assigned explicitly rather than declared as constructor
  // parameter properties: the project builds with erasableSyntaxOnly, which
  // rejects any TypeScript syntax that emits runtime code.
  constructor(
    status: number,
    message: string,
    fieldErrors?: Record<string, string[]>,
    body?: unknown,
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
    this.body = body
  }

  /** Flattens fieldErrors into lines suitable for showing under a form. */
  get details(): string[] {
    if (!this.fieldErrors) return []
    return Object.entries(this.fieldErrors).flatMap(([field, messages]) =>
      messages.map((m) => (field === '' ? m : `${field}: ${m}`)),
    )
  }
}

interface RequestOptions {
  method?: string
  body?: unknown
  signal?: AbortSignal
  /** Send the bearer token if we have one. On by default. */
  auth?: boolean
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, signal, auth = true } = options

  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (auth && authToken) headers.Authorization = `Bearer ${authToken}`

  let response: Response
  try {
    response = await fetch(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    })
  } catch (error) {
    // An aborted request is a normal part of effect cleanup, not a failure to
    // report. Re-thrown as-is so callers can recognise it by name.
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new ApiError(0, 'Could not reach the server. Is the API running?')
  }

  if (response.status === 204) return undefined as T

  const payload = await readBody(response)

  if (!response.ok) {
    // A 401 on any request means the token is gone or expired. Clearing it here
    // rather than in each caller means one expired token cannot leave half the
    // app thinking it is still signed in.
    if (response.status === 401) onUnauthorized?.()

    throw new ApiError(
      response.status,
      problemTitle(payload) ?? `Request failed (${response.status})`,
      fieldErrorsOf(payload),
      payload,
    )
  }

  return payload as T
}

async function readBody(response: Response): Promise<unknown> {
  const text = await response.text()
  if (text.length === 0) return undefined

  try {
    return JSON.parse(text)
  } catch {
    // Not JSON — an unhandled server exception page, or a proxy error. The raw
    // text is more useful in the message than "unexpected token < in JSON".
    return text
  }
}

/**
 * ASP.NET writes ProblemDetails for Problem() results and for model validation.
 * Model validation puts the useful part in `errors`, and a bare `title` there
 * reads "One or more validation errors occurred" — true but not worth showing.
 */
function problemTitle(payload: unknown): string | undefined {
  if (typeof payload === 'string') return payload || undefined
  if (!payload || typeof payload !== 'object') return undefined

  const problem = payload as { title?: unknown; detail?: unknown; message?: unknown }
  for (const value of [problem.detail, problem.message, problem.title]) {
    if (typeof value === 'string' && value.length > 0) return value
  }
  return undefined
}

function fieldErrorsOf(payload: unknown): Record<string, string[]> | undefined {
  if (!payload || typeof payload !== 'object') return undefined

  const errors = (payload as { errors?: unknown }).errors
  if (!errors || typeof errors !== 'object') return undefined

  const result: Record<string, string[]> = {}
  for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
    if (Array.isArray(value)) result[key] = value.map(String)
  }
  return Object.keys(result).length > 0 ? result : undefined
}

/** Builds a query string, dropping empty values and repeating array keys. */
export function toQuery(params: Record<string, unknown>): string {
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue

    if (Array.isArray(value)) {
      // Repeated keys rather than a comma-joined string: the API accepts both,
      // but repetition survives a value that itself contains a comma.
      for (const item of value) {
        if (item !== undefined && item !== null && item !== '') search.append(key, String(item))
      }
    } else if (typeof value === 'boolean') {
      if (value) search.append(key, 'true')
    } else {
      search.append(key, String(value))
    }
  }

  const query = search.toString()
  return query ? `?${query}` : ''
}

export const http = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>(path, { signal }),
  post: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: 'POST', body, signal }),
  put: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: 'PUT', body, signal }),
  delete: <T>(path: string, signal?: AbortSignal) =>
    request<T>(path, { method: 'DELETE', signal }),
}
