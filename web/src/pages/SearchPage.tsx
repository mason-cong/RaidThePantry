import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { searchRecipes } from '../api/recipes'
import type { Difficulty, RecipeSearchParams, RecipeSummary } from '../api/types'
import { CollapsibleFilters, type Filters } from '../components/FilterSidebar'
import { Pagination } from '../components/Pagination'
import { RecipeCard } from '../components/RecipeCard'
import { EmptyState, ErrorBanner, Spinner, inputClass } from '../components/Ui'
import { useApi, useDebounced } from '../hooks/useApi'
import { useAuth } from '../auth/useAuth'

/**
 * The filter state lives in the URL, not in component state.
 *
 * That is what makes a filtered search linkable and the back button work. The
 * alternative — useState here — looks simpler until someone shares a search and
 * the recipient gets the unfiltered list.
 */
export function SearchPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { user } = useAuth()

  const filters: Filters = {
    cuisines: searchParams.getAll('cuisine'),
    ingredients: searchParams.getAll('ing'),
    exactIngredientMatch: searchParams.get('exact') === 'true',
    difficulty: (searchParams.get('difficulty') as Difficulty | null) || null,
    maxTotalTimeMinutes: numberOrNull(searchParams.get('maxTime')),
  }

  const page = Math.max(1, Number(searchParams.get('page')) || 1)
  const sortBy = (searchParams.get('sort') as 'time' | 'title' | null) ?? null
  const queryText = searchParams.get('q') ?? ''

  // The text box updates as you type but only reaches the URL once you pause,
  // so a search does not push a history entry per keystroke.
  const [searchText, setSearchText] = useState(queryText)
  const debouncedText = useDebounced(searchText, 350)

  useEffect(() => {
    if (debouncedText !== queryText) {
      updateParams({ q: debouncedText || null, page: null }, { replace: true })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedText])

  // Back/forward navigation changes the URL underneath us; follow it.
  useEffect(() => {
    setSearchText(queryText)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [queryText])

  function updateParams(
    changes: Record<string, string | string[] | null>,
    options: { replace?: boolean } = {},
  ) {
    const next = new URLSearchParams(searchParams)

    for (const [key, value] of Object.entries(changes)) {
      next.delete(key)
      if (Array.isArray(value)) value.forEach((v) => next.append(key, v))
      else if (value !== null && value !== '') next.set(key, value)
    }

    setSearchParams(next, { replace: options.replace ?? false })
  }

  const applyFilters = (changes: Partial<Filters>) => {
    const merged = { ...filters, ...changes }
    updateParams({
      cuisine: merged.cuisines,
      ing: merged.ingredients,
      exact: merged.exactIngredientMatch ? 'true' : null,
      difficulty: merged.difficulty,
      maxTime: merged.maxTotalTimeMinutes ? String(merged.maxTotalTimeMinutes) : null,
      // Any filter change invalidates the current page number: page 3 of the
      // old result set is very unlikely to exist in the new one.
      page: null,
    })
  }

  const query: RecipeSearchParams = {
    search: queryText || undefined,
    cuisines: filters.cuisines,
    ingredients: filters.ingredients,
    exactIngredientMatch: filters.exactIngredientMatch,
    difficulty: filters.difficulty ?? undefined,
    maxTotalTimeMinutes: filters.maxTotalTimeMinutes ?? undefined,
    sortBy: sortBy ?? undefined,
    page,
    pageSize: 12,
  }

  // The serialized query is the dependency: it changes exactly when the request
  // would differ. `user` is in there because isFavorited depends on who asks —
  // signing in has to re-run the search or every heart stays empty.
  const key = JSON.stringify(query) + (user?.id ?? 'anon')
  const results = useApi((signal) => searchRecipes(query, signal), [key])

  // A local copy so a heart toggled on a card is not undone by the cached
  // response the next render reads from.
  const [recipes, setRecipes] = useState<RecipeSummary[]>([])
  useEffect(() => {
    if (results.data) setRecipes(results.data.items)
  }, [results.data])

  const onFavoriteChange = (recipeId: string, favorited: boolean) =>
    setRecipes((current) =>
      current.map((r) => (r.id === recipeId ? { ...r, isFavorited: favorited } : r)),
    )

  return (
    <div>
      <section className="hero-doodles mb-8 rounded-2xl bg-linear-to-br from-brand-500 to-brand-700 px-6 py-10 text-white shadow-sm">
        <h1 className="font-display text-3xl font-bold sm:text-4xl">What can you cook tonight?</h1>
        <p className="mt-2 max-w-xl text-brand-100">
          Search by name, or add the ingredients you already have and find recipes that use all of
          them.
        </p>

        <div className="mt-6">
          <label htmlFor="recipe-search" className="sr-only">
            Search recipes by title
          </label>
          <input
            id="recipe-search"
            type="search"
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            placeholder="Search recipes…"
            // bg-white is doing the real work: with no background class at all
            // the input was transparent, so the orange gradient showed straight
            // through and only the shadow hinted there was a field there.
            className="w-full max-w-xl rounded-xl border-0 bg-white px-4 py-3 text-ink-900 shadow-lg ring-1 ring-black/5 placeholder:text-ink-500 focus:outline-none"
          />
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-[16rem_1fr]">
        <CollapsibleFilters
          filters={filters}
          onChange={applyFilters}
          onReset={() => setSearchParams(queryText ? { q: queryText } : {})}
        />

        <section>
          <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-ink-500" aria-live="polite">
              {results.loading
                ? 'Searching…'
                : results.data
                  ? `${results.data.totalCount} recipe${results.data.totalCount === 1 ? '' : 's'}`
                  : ''}
            </p>

            <label className="flex items-center gap-2 text-sm text-ink-500">
              Sort
              <select
                className={`${inputClass} w-auto py-1.5`}
                value={sortBy ?? ''}
                onChange={(event) => updateParams({ sort: event.target.value || null, page: null })}
              >
                <option value="">Newest</option>
                <option value="time">Quickest</option>
                <option value="title">A–Z</option>
              </select>
            </label>
          </div>

          {results.error && <ErrorBanner message={results.error} onRetry={results.reload} />}

          {results.loading && recipes.length === 0 && <Spinner label="Finding recipes" />}

          {!results.loading && !results.error && recipes.length === 0 && (
            <EmptyState title="No recipes match those filters" icon="🔍">
              Try removing an ingredient, or widening the time limit.
            </EmptyState>
          )}

          {recipes.length > 0 && (
            <>
              <div
                // Dimmed rather than replaced while a new page loads, so the
                // layout does not collapse and jump under the cursor.
                className={`grid gap-5 transition-opacity sm:grid-cols-2 xl:grid-cols-3 ${
                  results.loading ? 'opacity-50' : ''
                }`}
              >
                {recipes.map((recipe) => (
                  <RecipeCard
                    key={recipe.id}
                    recipe={recipe}
                    onFavoriteChange={onFavoriteChange}
                  />
                ))}
              </div>

              <div className="mt-8">
                <Pagination
                  page={results.data?.page ?? page}
                  totalPages={results.data?.totalPages ?? 1}
                  totalCount={results.data?.totalCount ?? 0}
                  onPageChange={(next) => {
                    updateParams({ page: next > 1 ? String(next) : null })
                    window.scrollTo({ top: 0, behavior: 'smooth' })
                  }}
                />
              </div>
            </>
          )}
        </section>
      </div>
    </div>
  )
}

function numberOrNull(value: string | null): number | null {
  const parsed = Number(value)
  return value && Number.isFinite(parsed) && parsed > 0 ? parsed : null
}
