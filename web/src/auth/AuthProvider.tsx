import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { getAuthToken, setAuthToken, setUnauthorizedHandler } from '../api/client'
import { getCurrentUser, login, register } from '../api/recipes'
import type { CurrentUser } from '../api/types'
import { AuthContext } from './context'

/**
 * Holds the signed-in account.
 *
 * The token lives in localStorage so a refresh does not sign you out. That
 * trades a real risk for real usability: anything that can run script on this
 * origin can read the token. It is the right call here — there is no refresh
 * token, so in-memory storage would mean re-authenticating on every reload —
 * but it is a trade, not a free choice, and it is why the app must never render
 * unsanitised HTML from a recipe.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [initializing, setInitializing] = useState(true)

  const signOut = useCallback(() => {
    setAuthToken(null)
    setUser(null)
  }, [])

  // A stored token may have expired while the tab was closed, so it is not
  // proof of a session. GET /api/auth/me is the cheap way to find out, and it
  // is why that endpoint exists.
  useEffect(() => {
    const controller = new AbortController()

    if (!getAuthToken()) {
      setInitializing(false)
      return
    }

    getCurrentUser(controller.signal)
      .then(setUser)
      .catch(() => setAuthToken(null))
      .finally(() => {
        if (!controller.signal.aborted) setInitializing(false)
      })

    return () => controller.abort()
  }, [])

  // Any 401 from anywhere in the app drops us to signed-out, so an expired
  // token cannot leave half the UI believing it still has a session.
  useEffect(() => {
    setUnauthorizedHandler(signOut)
    return () => setUnauthorizedHandler(null)
  }, [signOut])

  const authenticate = async (
    action: (email: string, password: string) => Promise<{ accessToken: string }>,
    email: string,
    password: string,
  ) => {
    const response = await action(email, password)
    setAuthToken(response.accessToken)

    // Fetch the account rather than decoding the token: the claims we would
    // read are the server's business, and this also proves the token works
    // before the UI commits to a signed-in state.
    setUser(await getCurrentUser())
  }

  return (
    <AuthContext
      value={{
        user,
        initializing,
        signIn: (email, password) => authenticate(login, email, password),
        signUp: (email, password) => authenticate(register, email, password),
        signOut,
      }}
    >
      {children}
    </AuthContext>
  )
}
