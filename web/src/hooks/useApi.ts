import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../api/client'

export interface AsyncState<T> {
  data: T | null
  loading: boolean
  error: string | null
  /** Re-runs the loader. Useful after a write that changes what was loaded. */
  reload: () => void
}

/**
 * Loads a value and holds it, along with its loading and error state.
 *
 * The loader receives an AbortSignal and the hook aborts on cleanup, which
 * matters for two reasons: StrictMode runs every effect twice in development,
 * and a fast typist changing filters can have several searches in flight at
 * once. Without the abort, whichever response happens to land last wins, and
 * the list can end up showing results for a query the user already replaced.
 *
 * `deps` is the dependency list, exactly as useEffect takes it. The loader
 * itself is deliberately not a dependency — callers write it inline, so it is a
 * new function every render and would loop forever.
 */
export function useApi<T>(
  loader: (signal: AbortSignal) => Promise<T>,
  deps: unknown[],
): AsyncState<T> {
  const [data, setData] = useState<T | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadCount, setReloadCount] = useState(0)

  const reload = useCallback(() => setReloadCount((n) => n + 1), [])

  useEffect(() => {
    const controller = new AbortController()
    let active = true

    setLoading(true)
    setError(null)

    loader(controller.signal)
      .then((result) => {
        if (active) setData(result)
      })
      .catch((cause) => {
        // An abort is this hook tearing down, not a failure worth showing.
        if (!active || controller.signal.aborted) return
        setError(messageOf(cause))
        setData(null)
      })
      .finally(() => {
        if (active) setLoading(false)
      })

    return () => {
      active = false
      controller.abort()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, reloadCount])

  return { data, loading, error, reload }
}

/**
 * The write-side counterpart: runs an action on demand and tracks whether it is
 * in flight and how it failed. Returns the action's result so the caller can
 * navigate with it.
 */
export function useAction<TArgs extends unknown[], TResult>(
  action: (...args: TArgs) => Promise<TResult>,
) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [details, setDetails] = useState<string[]>([])

  const run = async (...args: TArgs): Promise<TResult | undefined> => {
    setPending(true)
    setError(null)
    setDetails([])
    try {
      return await action(...args)
    } catch (cause) {
      setError(messageOf(cause))
      setDetails(cause instanceof ApiError ? cause.details : [])
      return undefined
    } finally {
      setPending(false)
    }
  }

  return { run, pending, error, details, clearError: () => setError(null) }
}

export function messageOf(cause: unknown): string {
  if (cause instanceof ApiError) return cause.message
  if (cause instanceof Error) return cause.message
  return 'Something went wrong.'
}

/**
 * Delays a rapidly-changing value. The search box and the ingredient
 * autocomplete both use it so that typing does not fire a request per keystroke.
 */
export function useDebounced<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}
