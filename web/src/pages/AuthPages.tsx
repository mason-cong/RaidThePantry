import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { Button, ErrorBanner, Field, inputClass } from '../components/Ui'
import { useAction } from '../hooks/useApi'

/** Matches Identity's RequiredLength and the MinLength on RegisterRequest. */
const MIN_PASSWORD_LENGTH = 10

function AuthForm({ mode }: { mode: 'login' | 'register' }) {
  const { user, signIn, signUp } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const isRegister = mode === 'register'
  // FavoriteButton sends people here mid-action; put them back afterwards.
  const returnTo = (location.state as { from?: string } | null)?.from ?? '/'

  const submit = useAction(async () => {
    await (isRegister ? signUp(email, password) : signIn(email, password))
    navigate(returnTo, { replace: true })
  })

  // Already signed in — nothing to do on this page.
  if (user) return <Navigate to={returnTo} replace />

  const tooShort = isRegister && password.length > 0 && password.length < MIN_PASSWORD_LENGTH

  return (
    <div className="mx-auto max-w-md">
      <div className="rounded-2xl border border-ink-200 bg-white p-8 shadow-sm">
        <h1 className="font-display text-2xl font-bold text-ink-900">
          {isRegister ? 'Create an account' : 'Welcome back'}
        </h1>
        <p className="mt-1 text-sm text-ink-500">
          {isRegister
            ? 'You only need one to save favorites and add recipes.'
            : 'Sign in to reach your favorites and your recipes.'}
        </p>

        <form
          className="mt-6 space-y-4"
          onSubmit={(event) => {
            event.preventDefault()
            submit.run()
          }}
        >
          <Field label="Email">
            <input
              type="email"
              required
              autoComplete="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              className={inputClass}
            />
          </Field>

          <Field
            label="Password"
            hint={
              isRegister
                ? `At least ${MIN_PASSWORD_LENGTH} characters. No symbol or capital required — length is what matters.`
                : undefined
            }
          >
            <input
              type="password"
              required
              minLength={isRegister ? MIN_PASSWORD_LENGTH : undefined}
              autoComplete={isRegister ? 'new-password' : 'current-password'}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              className={inputClass}
            />
          </Field>

          {tooShort && (
            <p className="text-xs text-amber-700">
              {MIN_PASSWORD_LENGTH - password.length} more character
              {MIN_PASSWORD_LENGTH - password.length === 1 ? '' : 's'} to go.
            </p>
          )}

          {submit.error && <ErrorBanner message={submit.error} details={submit.details} />}

          <Button type="submit" disabled={submit.pending} className="w-full">
            {submit.pending ? 'Please wait…' : isRegister ? 'Create account' : 'Sign in'}
          </Button>
        </form>

        <p className="mt-6 text-center text-sm text-ink-500">
          {isRegister ? (
            <>
              Already have an account?{' '}
              <Link to="/login" state={location.state} className="font-medium text-brand-600 hover:underline">
                Sign in
              </Link>
            </>
          ) : (
            <>
              New here?{' '}
              <Link to="/register" state={location.state} className="font-medium text-brand-600 hover:underline">
                Create an account
              </Link>
            </>
          )}
        </p>
      </div>

      <p className="mt-4 text-center text-xs text-ink-400">
        Browsing recipes never requires an account.
      </p>
    </div>
  )
}

export const LoginPage = () => <AuthForm mode="login" />
export const RegisterPage = () => <AuthForm mode="register" />
