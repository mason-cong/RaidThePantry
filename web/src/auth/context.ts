import { createContext } from 'react'
import type { CurrentUser } from '../api/types'

export interface AuthState {
  user: CurrentUser | null
  /** True until the stored token has been checked against the server. */
  initializing: boolean
  signIn: (email: string, password: string) => Promise<void>
  signUp: (email: string, password: string) => Promise<void>
  signOut: () => void
}

/**
 * In its own module so this file exports only a value and Vite's fast refresh
 * can still hot-reload the provider component next to it.
 */
export const AuthContext = createContext<AuthState | null>(null)
