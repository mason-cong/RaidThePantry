import { Link } from 'react-router-dom'
import type { RecipeSummary } from '../api/types'
import { FavoriteButton } from './FavoriteButton'
import { Badge, formatMinutes } from './Ui'

const DIFFICULTY_STYLES: Record<string, string> = {
  Easy: 'bg-emerald-100 text-emerald-700',
  Medium: 'bg-amber-100 text-amber-700',
  Hard: 'bg-rose-100 text-rose-700',
}

export function RecipeCard({
  recipe,
  onFavoriteChange,
}: {
  recipe: RecipeSummary
  onFavoriteChange?: (recipeId: string, favorited: boolean) => void
}) {
  return (
    <article className="group relative overflow-hidden rounded-xl border border-ink-200 bg-white shadow-sm transition hover:-translate-y-0.5 hover:shadow-md">
      <div className="absolute right-3 top-3 z-10">
        <FavoriteButton
          recipeId={recipe.id}
          isFavorited={recipe.isFavorited}
          onChange={(favorited) => onFavoriteChange?.(recipe.id, favorited)}
          size="sm"
        />
      </div>

      <Link to={`/recipes/${recipe.id}`} className="block">
        <div className="aspect-[4/3] overflow-hidden bg-ink-100">
          {recipe.imageUrl ? (
            <img
              src={recipe.imageUrl}
              alt=""
              loading="lazy"
              className="size-full object-cover transition duration-300 group-hover:scale-105"
              // Scraped images point at other people's servers and some of them
              // 404. Hiding a broken image beats showing the browser's icon.
              onError={(event) => {
                event.currentTarget.style.display = 'none'
              }}
            />
          ) : (
            <div className="grid size-full place-items-center text-4xl" aria-hidden="true">
              🥘
            </div>
          )}
        </div>

        <div className="p-4">
          <h3 className="line-clamp-2 font-display text-lg leading-snug font-semibold text-ink-900 group-hover:text-brand-600">
            {recipe.title}
          </h3>

          <div className="mt-3 flex flex-wrap items-center gap-2 text-xs">
            <span
              className={`rounded-full px-2.5 py-0.5 font-medium ${
                DIFFICULTY_STYLES[recipe.difficulty] ?? 'bg-ink-100 text-ink-700'
              }`}
            >
              {recipe.difficulty}
            </span>
            <span className="text-ink-500">⏱ {formatMinutes(recipe.totalTimeMinutes)}</span>
          </div>

          {recipe.cuisines.length > 0 && (
            <div className="mt-3 flex flex-wrap gap-1.5">
              {recipe.cuisines.slice(0, 3).map((cuisine) => (
                <Badge key={cuisine}>{cuisine}</Badge>
              ))}
            </div>
          )}
        </div>
      </Link>
    </article>
  )
}
