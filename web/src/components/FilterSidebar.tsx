import { useEffect, useState } from 'react'
import { getCuisines, searchIngredients } from '../api/recipes'
import { DIFFICULTIES, type Difficulty } from '../api/types'
import { useApi, useDebounced } from '../hooks/useApi'
import { Button, Field, inputClass } from './Ui'

export interface Filters {
  cuisines: string[]
  ingredients: string[]
  exactIngredientMatch: boolean
  difficulty: Difficulty | null
  maxTotalTimeMinutes: number | null
}

const TIME_OPTIONS = [15, 30, 45, 60, 90]

export function FilterSidebar({
  filters,
  onChange,
  onReset,
}: {
  filters: Filters
  onChange: (next: Partial<Filters>) => void
  onReset: () => void
}) {
  // Cuisines are a small, slow-changing list — load once and hold.
  const cuisines = useApi((signal) => getCuisines(signal), [])

  const activeCount =
    filters.cuisines.length +
    filters.ingredients.length +
    (filters.difficulty ? 1 : 0) +
    (filters.maxTotalTimeMinutes ? 1 : 0)

  const toggleCuisine = (name: string) =>
    onChange({
      cuisines: filters.cuisines.includes(name)
        ? filters.cuisines.filter((c) => c !== name)
        : [...filters.cuisines, name],
    })

  return (
    <aside className="space-y-6 rounded-xl border border-ink-200 bg-white p-5 shadow-sm">
      <div className="flex items-center justify-between">
        <h2 className="font-semibold text-ink-900">Filters</h2>
        {activeCount > 0 && (
          <button
            type="button"
            onClick={onReset}
            className="text-xs font-medium text-brand-600 hover:underline"
          >
            Clear all ({activeCount})
          </button>
        )}
      </div>

      <IngredientPicker
        selected={filters.ingredients}
        exact={filters.exactIngredientMatch}
        onChange={(ingredients) => onChange({ ingredients })}
        onExactChange={(exactIngredientMatch) => onChange({ exactIngredientMatch })}
      />

      <div>
        <h3 className="mb-2 text-sm font-medium text-ink-700">Cuisine</h3>
        {cuisines.loading && <p className="text-xs text-ink-400">Loading…</p>}
        <div className="flex flex-wrap gap-1.5">
          {(cuisines.data ?? []).map((cuisine) => {
            const active = filters.cuisines.includes(cuisine.name)
            return (
              <button
                key={cuisine.id}
                type="button"
                onClick={() => toggleCuisine(cuisine.name)}
                aria-pressed={active}
                className={`rounded-full px-3 py-1 text-xs font-medium transition ${
                  active
                    ? 'bg-brand-500 text-white'
                    : 'bg-ink-100 text-ink-700 hover:bg-ink-200'
                }`}
              >
                {cuisine.name}
                <span className={active ? 'ml-1 opacity-75' : 'ml-1 text-ink-400'}>
                  {cuisine.recipeCount}
                </span>
              </button>
            )
          })}
        </div>
      </div>

      <Field label="Difficulty">
        <select
          className={inputClass}
          value={filters.difficulty ?? ''}
          onChange={(event) =>
            onChange({ difficulty: (event.target.value || null) as Difficulty | null })
          }
        >
          <option value="">Any</option>
          {DIFFICULTIES.map((level) => (
            <option key={level} value={level}>
              {level}
            </option>
          ))}
        </select>
      </Field>

      <div>
        <h3 className="mb-2 text-sm font-medium text-ink-700">Ready in under</h3>
        <div className="flex flex-wrap gap-1.5">
          {TIME_OPTIONS.map((minutes) => {
            const active = filters.maxTotalTimeMinutes === minutes
            return (
              <button
                key={minutes}
                type="button"
                // Clicking the active one clears it, so the filter can be
                // removed without hunting for a separate control.
                onClick={() => onChange({ maxTotalTimeMinutes: active ? null : minutes })}
                aria-pressed={active}
                className={`rounded-lg px-3 py-1.5 text-xs font-medium transition ${
                  active ? 'bg-brand-500 text-white' : 'bg-ink-100 text-ink-700 hover:bg-ink-200'
                }`}
              >
                {minutes} min
              </button>
            )
          })}
        </div>
      </div>
    </aside>
  )
}

/**
 * Ingredient chips with autocomplete.
 *
 * Every chip narrows the results further — the API ANDs them, so "chicken" plus
 * "lemon" means both, not either. The label says so, because a user who assumes
 * OR will read an empty result as a broken search.
 */
function IngredientPicker({
  selected,
  exact,
  onChange,
  onExactChange,
}: {
  selected: string[]
  exact: boolean
  onChange: (ingredients: string[]) => void
  onExactChange: (exact: boolean) => void
}) {
  const [term, setTerm] = useState('')
  const [open, setOpen] = useState(false)
  const debouncedTerm = useDebounced(term, 250)

  const suggestions = useApi(
    (signal) =>
      debouncedTerm.trim().length < 2
        ? Promise.resolve([])
        : searchIngredients(debouncedTerm.trim(), 8, signal),
    [debouncedTerm],
  )

  const add = (name: string) => {
    const value = name.trim().toLowerCase()
    if (value && !selected.includes(value)) onChange([...selected, value])
    setTerm('')
    setOpen(false)
  }

  // Close the suggestion list when the term is cleared, so it does not linger
  // over the filters below it.
  useEffect(() => {
    if (term.trim().length < 2) setOpen(false)
  }, [term])

  return (
    <div>
      <h3 className="mb-2 text-sm font-medium text-ink-700">
        Ingredients <span className="font-normal text-ink-400">(all must match)</span>
      </h3>

      <div className="relative">
        <input
          type="text"
          value={term}
          placeholder="e.g. chicken"
          className={inputClass}
          onChange={(event) => {
            setTerm(event.target.value)
            setOpen(true)
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault()
              add(term)
            } else if (event.key === 'Escape') {
              setOpen(false)
            }
          }}
          // A click on a suggestion has to register before the list closes,
          // hence the delay rather than closing on blur directly.
          onBlur={() => setTimeout(() => setOpen(false), 150)}
        />

        {open && (suggestions.data?.length ?? 0) > 0 && (
          <ul className="absolute z-10 mt-1 max-h-56 w-full overflow-auto rounded-lg border border-ink-200 bg-white py-1 shadow-lg">
            {suggestions.data!.map((ingredient) => (
              <li key={ingredient.id}>
                <button
                  type="button"
                  onMouseDown={() => add(ingredient.name)}
                  className="flex w-full items-center justify-between px-3 py-1.5 text-left text-sm hover:bg-ink-100"
                >
                  <span>{ingredient.name}</span>
                  <span className="text-xs text-ink-400">{ingredient.recipeCount}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {selected.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {selected.map((name) => (
            <span
              key={name}
              className="inline-flex items-center gap-1 rounded-full bg-brand-100 py-1 pl-3 pr-1 text-xs font-medium text-brand-700"
            >
              {name}
              <button
                type="button"
                onClick={() => onChange(selected.filter((i) => i !== name))}
                aria-label={`Remove ${name}`}
                className="grid size-4 place-items-center rounded-full hover:bg-brand-200"
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}

      <label className="mt-2 flex items-center gap-2 text-xs text-ink-500">
        <input
          type="checkbox"
          checked={exact}
          onChange={(event) => onExactChange(event.target.checked)}
          className="rounded border-ink-300 text-brand-500"
        />
        Match whole ingredient names only
      </label>
    </div>
  )
}

/** Mobile: the sidebar collapses behind a toggle rather than pushing results down. */
export function CollapsibleFilters(props: Parameters<typeof FilterSidebar>[0]) {
  const [open, setOpen] = useState(false)

  return (
    <>
      <div className="lg:hidden">
        <Button variant="secondary" onClick={() => setOpen((v) => !v)} className="w-full">
          {open ? 'Hide filters' : 'Show filters'}
        </Button>
        {open && (
          <div className="mt-3">
            <FilterSidebar {...props} />
          </div>
        )}
      </div>

      <div className="hidden lg:block">
        <FilterSidebar {...props} />
      </div>
    </>
  )
}
