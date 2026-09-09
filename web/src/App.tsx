import type { ReactNode } from 'react'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './auth/AuthProvider'
import { useAuth } from './auth/useAuth'
import { Layout } from './components/Layout'
import { EmptyState, Spinner } from './components/Ui'
import { LoginPage, RegisterPage } from './pages/AuthPages'
import { FavoritesPage } from './pages/FavoritesPage'
import { ImportPage } from './pages/ImportPage'
import { RecipeDetailPage, SignInRequired } from './pages/RecipeDetailPage'
import { RecipeFormPage } from './pages/RecipeFormPage'
import { SearchPage } from './pages/SearchPage'

/**
 * Gates the routes that need an account.
 *
 * This is a convenience, not a security boundary — the API enforces the same
 * rule and is the only place that can. Blocking here just avoids rendering a
 * page that could only ever produce a 401.
 */
function RequireAuth({ children }: { children: ReactNode }) {
  const { user, initializing } = useAuth()

  // Without this, a reload on /favorites redirects to sign-in before the stored
  // token has been checked — the user gets bounced out of their own session.
  if (initializing) return <Spinner label="Checking your session" />

  return user ? <>{children}</> : <SignInRequired />
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<SearchPage />} />
            <Route path="recipes/:id" element={<RecipeDetailPage />} />

            <Route path="login" element={<LoginPage />} />
            <Route path="register" element={<RegisterPage />} />

            <Route
              path="recipes/new"
              element={
                <RequireAuth>
                  <RecipeFormPage />
                </RequireAuth>
              }
            />
            <Route
              path="recipes/:id/edit"
              element={
                <RequireAuth>
                  <RecipeFormPage />
                </RequireAuth>
              }
            />
            <Route
              path="favorites"
              element={
                <RequireAuth>
                  <FavoritesPage />
                </RequireAuth>
              }
            />
            <Route
              path="import"
              element={
                <RequireAuth>
                  <ImportPage />
                </RequireAuth>
              }
            />

            <Route
              path="*"
              element={<EmptyState title="That page doesn't exist" icon="🤷" />}
            />
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}
