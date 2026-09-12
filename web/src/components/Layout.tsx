import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { Button } from './Ui'

function navClass({ isActive }: { isActive: boolean }) {
  return `rounded-lg px-3 py-2 text-sm font-medium transition ${
    isActive ? 'bg-brand-100 text-brand-700' : 'text-ink-500 hover:bg-ink-100 hover:text-ink-900'
  }`
}

export function Layout() {
  const { user, signOut, initializing } = useAuth()
  const navigate = useNavigate()

  return (
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-20 border-b border-ink-200 bg-white/80 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center gap-2 px-4 py-3">
          <Link to="/" className="mr-auto flex items-center gap-2">
            <span className="text-2xl" aria-hidden="true">
              🍲
            </span>
            {/* The brand, which is deliberately not the project name. The repo,
                the .NET projects and the database still say RecipeApi — renaming
                those would touch hundreds of files that no visitor ever sees. */}
            <span className="font-display text-xl font-bold tracking-tight text-ink-900">
              Raid the Pantry
            </span>
          </Link>

          <nav className="flex items-center gap-1">
            <NavLink to="/" end className={navClass}>
              Browse
            </NavLink>

            {/* Contributing needs an account, so these only appear once signed
                in — better than showing them and failing with a 401. */}
            {user && (
              <>
                <NavLink to="/favorites" className={navClass}>
                  Favorites
                </NavLink>
                <NavLink to="/recipes/new" className={navClass}>
                  Add
                </NavLink>
                <NavLink to="/import" className={navClass}>
                  Import
                </NavLink>
              </>
            )}
          </nav>

          <div className="ml-2 flex items-center gap-2 border-l border-ink-200 pl-3">
            {initializing ? (
              <span className="text-sm text-ink-400">…</span>
            ) : user ? (
              <>
                <span className="hidden max-w-[14rem] truncate text-sm text-ink-500 sm:inline">
                  {user.email}
                </span>
                <Button
                  variant="ghost"
                  onClick={() => {
                    signOut()
                    navigate('/')
                  }}
                >
                  Sign out
                </Button>
              </>
            ) : (
              <>
                <Link to="/login" className={navClass({ isActive: false })}>
                  Sign in
                </Link>
                <Link
                  to="/register"
                  className="rounded-lg bg-brand-500 px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-brand-600"
                >
                  Create account
                </Link>
              </>
            )}
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-6xl grow px-4 py-8">
        <Outlet />
      </main>

      <footer className="border-t border-ink-200 bg-white">
        <div className="mx-auto max-w-6xl px-4 py-6 text-sm text-ink-400">
          Browsing needs no account — sign in only to save favorites or add recipes.
        </div>
      </footer>
    </div>
  )
}
