import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getFavorites } from '../api/recipes'
import type { RecipeSummary } from '../api/types'
import { Pagination } from '../components/Pagination'
import { RecipeCard } from '../components/RecipeCard'
import { EmptyState, ErrorBanner, Spinner } from '../components/Ui'
import { useApi } from '../hooks/useApi'

export function FavoritesPage() {
  const [page, setPage] = useState(1)
  const favorites = useApi((signal) => getFavorites(page, 12, signal), [page])

  const [recipes, setRecipes] = useState<RecipeSummary[]>([])
  useEffect(() => {
    if (favorites.data) setRecipes(favorites.data.items)
  }, [favorites.data])

  return (
    <div>
      <h1 className="font-display text-3xl font-bold text-ink-900">Saved recipes</h1>
      <p className="mt-1 text-sm text-ink-500">Most recently saved first.</p>

      <div className="mt-6 space-y-6">
        {favorites.error && <ErrorBanner message={favorites.error} onRetry={favorites.reload} />}

        {favorites.loading && recipes.length === 0 && <Spinner label="Loading favorites" />}

        {!favorites.loading && !favorites.error && recipes.length === 0 && (
          <EmptyState title="Nothing saved yet" icon="♡">
            Tap the heart on any recipe to keep it here.{' '}
            <Link to="/" className="font-medium text-brand-600 hover:underline">
              Browse recipes
            </Link>
          </EmptyState>
        )}

        {recipes.length > 0 && (
          <>
            <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
              {recipes.map((recipe) => (
                <RecipeCard
                  key={recipe.id}
                  recipe={recipe}
                  // Unsaving from this page should remove the card, not leave a
                  // hollow heart on a list that is defined by being saved.
                  onFavoriteChange={(recipeId, favorited) => {
                    if (!favorited) setRecipes((current) => current.filter((r) => r.id !== recipeId))
                  }}
                />
              ))}
            </div>

            <Pagination
              page={favorites.data?.page ?? page}
              totalPages={favorites.data?.totalPages ?? 1}
              totalCount={favorites.data?.totalCount ?? 0}
              onPageChange={setPage}
            />
          </>
        )}
      </div>
    </div>
  )
}
