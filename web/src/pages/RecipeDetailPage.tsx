import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { deleteRecipe, getRecipe } from '../api/recipes'
import { FavoriteButton } from '../components/FavoriteButton'
import { Badge, Button, EmptyState, ErrorBanner, Spinner, formatMinutes } from '../components/Ui'
import { useAction, useApi } from '../hooks/useApi'
import { useAuth } from '../auth/useAuth'

export function RecipeDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { user } = useAuth()

  // `user` is a dependency because isFavorited and isEditable both depend on
  // who is asking — the same recipe genuinely has a different body per caller.
  const recipe = useApi((signal) => getRecipe(id, signal), [id, user?.id ?? 'anon'])

  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const remove = useAction(async () => {
    await deleteRecipe(id)
    navigate('/', { replace: true })
  })

  if (recipe.loading) return <Spinner label="Loading recipe" />

  if (recipe.error || !recipe.data) {
    return (
      <div className="space-y-4">
        <ErrorBanner message={recipe.error ?? 'Recipe not found.'} onRetry={recipe.reload} />
        <Link to="/" className="text-sm font-medium text-brand-600 hover:underline">
          ← Back to all recipes
        </Link>
      </div>
    )
  }

  const data = recipe.data
  const totalTime = data.prepTimeMinutes + data.cookTimeMinutes

  return (
    <article className="mx-auto max-w-4xl">
      <Link to="/" className="text-sm font-medium text-brand-600 hover:underline">
        ← Back to all recipes
      </Link>

      <header className="mt-4">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <h1 className="font-display text-3xl font-bold text-ink-900 sm:text-4xl">{data.title}</h1>
          <FavoriteButton recipeId={data.id} isFavorited={data.isFavorited} />
        </div>

        {data.description && <p className="mt-3 text-ink-500">{data.description}</p>}

        <div className="mt-4 flex flex-wrap gap-1.5">
          {data.cuisines.map((cuisine) => (
            <Badge key={cuisine}>{cuisine}</Badge>
          ))}
          {data.tags.map((tag) => (
            <span
              key={tag}
              className="rounded-full bg-ink-100 px-2.5 py-0.5 text-xs font-medium text-ink-700"
            >
              {tag}
            </span>
          ))}
        </div>
      </header>

      {data.imageUrl && (
        <img
          src={data.imageUrl}
          alt=""
          className="mt-6 aspect-video w-full rounded-xl object-cover shadow-sm"
          onError={(event) => {
            event.currentTarget.style.display = 'none'
          }}
        />
      )}

      <dl className="mt-6 grid grid-cols-2 gap-3 rounded-xl border border-ink-200 bg-white p-4 sm:grid-cols-4">
        <Stat label="Prep" value={formatMinutes(data.prepTimeMinutes)} />
        <Stat label="Cook" value={formatMinutes(data.cookTimeMinutes)} />
        <Stat label="Total" value={formatMinutes(totalTime)} />
        <Stat label="Serves" value={String(data.servings)} />
      </dl>

      <div className="mt-8 grid gap-8 md:grid-cols-[18rem_1fr]">
        <section>
          <h2 className="font-display text-xl font-semibold text-ink-900">Ingredients</h2>
          <ul className="mt-3 space-y-2">
            {data.ingredients.map((ingredient, index) => (
              <li
                // Ingredient names are not unique within a recipe ("salt" can
                // appear in two components), so the index is part of the key.
                key={`${ingredient.name}-${index}`}
                className="flex gap-2 rounded-lg bg-white px-3 py-2 text-sm shadow-sm"
              >
                <span className="text-brand-500" aria-hidden="true">
                  •
                </span>
                {/* rawText is what the recipe actually said; the normalized
                    name is for searching, and reads oddly in a method. */}
                <span>{ingredient.rawText || describe(ingredient)}</span>
              </li>
            ))}
          </ul>
        </section>

        <section>
          <h2 className="font-display text-xl font-semibold text-ink-900">Method</h2>
          <ol className="mt-3 space-y-4">
            {data.steps.map((step, index) => (
              <li key={index} className="flex gap-4">
                <span className="grid size-8 shrink-0 place-items-center rounded-full bg-brand-100 text-sm font-semibold text-brand-700">
                  {index + 1}
                </span>
                <p className="pt-1 text-ink-700">{step}</p>
              </li>
            ))}
          </ol>
        </section>
      </div>

      {data.sourceUrl && (
        <p className="mt-8 text-sm text-ink-400">
          Source:{' '}
          <a
            href={data.sourceUrl}
            target="_blank"
            // noreferrer as well as noopener: the target page has no business
            // knowing which page linked to it.
            rel="noopener noreferrer"
            className="text-brand-600 hover:underline"
          >
            {hostOf(data.sourceUrl)}
          </a>
        </p>
      )}

      {/* isEditable comes from the server, which owns the ownership rule —
          scraped recipes have no owner and are editable by nobody. */}
      {data.isEditable && (
        <div className="mt-8 flex flex-wrap gap-2 border-t border-ink-200 pt-6">
          <Button variant="secondary" onClick={() => navigate(`/recipes/${data.id}/edit`)}>
            Edit recipe
          </Button>

          {confirmingDelete ? (
            <>
              <Button variant="danger" disabled={remove.pending} onClick={() => remove.run()}>
                {remove.pending ? 'Deleting…' : 'Really delete'}
              </Button>
              <Button variant="ghost" onClick={() => setConfirmingDelete(false)}>
                Cancel
              </Button>
            </>
          ) : (
            <Button variant="ghost" onClick={() => setConfirmingDelete(true)}>
              Delete
            </Button>
          )}
        </div>
      )}

      {remove.error && (
        <div className="mt-4">
          <ErrorBanner message={remove.error} />
        </div>
      )}
    </article>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium tracking-wide text-ink-400 uppercase">{label}</dt>
      <dd className="mt-0.5 font-semibold text-ink-900">{value}</dd>
    </div>
  )
}

function describe(ingredient: { name: string; quantity: number | null; unit: string | null }) {
  return [ingredient.quantity, ingredient.unit, ingredient.name].filter(Boolean).join(' ')
}

function hostOf(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '')
  } catch {
    return url
  }
}

/** Shown when a route needs an account and there isn't one. */
export function SignInRequired() {
  return (
    <EmptyState title="You need an account for that" icon="🔐">
      <Link to="/login" className="font-medium text-brand-600 hover:underline">
        Sign in
      </Link>{' '}
      or{' '}
      <Link to="/register" className="font-medium text-brand-600 hover:underline">
        create an account
      </Link>
      .
    </EmptyState>
  )
}
