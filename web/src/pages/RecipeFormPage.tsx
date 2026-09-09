import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { createRecipe, getRecipe, updateRecipe } from '../api/recipes'
import { DIFFICULTIES, type CreateRecipeRequest, type Difficulty } from '../api/types'
import { Button, ErrorBanner, Field, Spinner, inputClass } from '../components/Ui'
import { useAction, useApi } from '../hooks/useApi'

/**
 * Create and edit share this component because PUT takes the identical body to
 * POST — a full replacement, not a patch. Two components would be two copies of
 * the same form drifting apart.
 */
interface FormState {
  title: string
  description: string
  prepTimeMinutes: string
  cookTimeMinutes: string
  servings: string
  difficulty: Difficulty
  imageUrl: string
  cuisines: string
  tags: string
  /** One free-text line each: "2 cloves garlic, crushed". The server normalizes. */
  ingredients: string[]
  steps: string[]
}

const EMPTY: FormState = {
  title: '',
  description: '',
  prepTimeMinutes: '10',
  cookTimeMinutes: '20',
  servings: '2',
  difficulty: 'Easy',
  imageUrl: '',
  cuisines: '',
  tags: '',
  ingredients: [''],
  steps: [''],
}

export function RecipeFormPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const isEdit = Boolean(id)

  const [form, setForm] = useState<FormState>(EMPTY)

  // Only loads in edit mode. The hook always runs, so the create path resolves
  // immediately rather than branching around the rules of hooks.
  const existing = useApi(
    (signal) => (id ? getRecipe(id, signal) : Promise.resolve(null)),
    [id ?? ''],
  )

  useEffect(() => {
    const recipe = existing.data
    if (!recipe) return

    setForm({
      title: recipe.title,
      description: recipe.description ?? '',
      prepTimeMinutes: String(recipe.prepTimeMinutes),
      cookTimeMinutes: String(recipe.cookTimeMinutes),
      servings: String(recipe.servings),
      difficulty: recipe.difficulty,
      imageUrl: recipe.imageUrl ?? '',
      cuisines: recipe.cuisines.join(', '),
      tags: recipe.tags.join(', '),
      // rawText is what the recipe actually said; the normalized name has had
      // its quantities stripped and would silently lose them on a re-save.
      ingredients: recipe.ingredients.map((i) => i.rawText || i.name),
      steps: [...recipe.steps],
    })
  }, [existing.data])

  const save = useAction(async () => {
    const body = toRequest(form)
    if (isEdit && id) {
      await updateRecipe(id, body)
      navigate(`/recipes/${id}`)
    } else {
      const newId = await createRecipe(body)
      navigate(`/recipes/${newId}`)
    }
  })

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((current) => ({ ...current, [key]: value }))

  if (isEdit && existing.loading) return <Spinner label="Loading recipe" />

  if (isEdit && existing.error) {
    return <ErrorBanner message={existing.error} onRetry={existing.reload} />
  }

  // The server enforces this too; catching it here avoids a pointless round trip.
  if (isEdit && existing.data && !existing.data.isEditable) {
    return (
      <ErrorBanner message="You can only edit recipes you created. Imported and scraped recipes have no owner." />
    )
  }

  return (
    <div className="mx-auto max-w-3xl">
      <h1 className="font-display text-3xl font-bold text-ink-900">
        {isEdit ? 'Edit recipe' : 'Add a recipe'}
      </h1>

      <form
        className="mt-6 space-y-6"
        onSubmit={(event) => {
          event.preventDefault()
          save.run()
        }}
      >
        <section className="space-y-4 rounded-xl border border-ink-200 bg-white p-5">
          <Field label="Title">
            <input
              required
              maxLength={300}
              value={form.title}
              onChange={(event) => set('title', event.target.value)}
              className={inputClass}
            />
          </Field>

          <Field label="Description" hint="Optional. One or two lines about the dish.">
            <textarea
              rows={3}
              maxLength={4000}
              value={form.description}
              onChange={(event) => set('description', event.target.value)}
              className={inputClass}
            />
          </Field>

          <div className="grid gap-4 sm:grid-cols-4">
            <Field label="Prep (min)">
              <input
                type="number"
                min={0}
                max={10080}
                required
                value={form.prepTimeMinutes}
                onChange={(event) => set('prepTimeMinutes', event.target.value)}
                className={inputClass}
              />
            </Field>
            <Field label="Cook (min)">
              <input
                type="number"
                min={0}
                max={10080}
                required
                value={form.cookTimeMinutes}
                onChange={(event) => set('cookTimeMinutes', event.target.value)}
                className={inputClass}
              />
            </Field>
            <Field label="Serves">
              <input
                type="number"
                min={1}
                max={1000}
                required
                value={form.servings}
                onChange={(event) => set('servings', event.target.value)}
                className={inputClass}
              />
            </Field>
            <Field label="Difficulty">
              <select
                value={form.difficulty}
                onChange={(event) => set('difficulty', event.target.value as Difficulty)}
                className={inputClass}
              >
                {DIFFICULTIES.map((level) => (
                  <option key={level} value={level}>
                    {level}
                  </option>
                ))}
              </select>
            </Field>
          </div>

          <Field label="Image URL" hint="Optional.">
            <input
              type="url"
              maxLength={2048}
              value={form.imageUrl}
              onChange={(event) => set('imageUrl', event.target.value)}
              className={inputClass}
            />
          </Field>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Cuisines" hint="Comma separated, e.g. Italian, Vegetarian">
              <input
                value={form.cuisines}
                onChange={(event) => set('cuisines', event.target.value)}
                className={inputClass}
              />
            </Field>
            <Field label="Tags" hint="Comma separated, e.g. weeknight, one-pot">
              <input
                value={form.tags}
                onChange={(event) => set('tags', event.target.value)}
                className={inputClass}
              />
            </Field>
          </div>
        </section>

        <ListEditor
          title="Ingredients"
          hint="One per line, as you would write them: “2 cloves garlic, crushed”."
          items={form.ingredients}
          placeholder="200 g spaghetti"
          onChange={(ingredients) => set('ingredients', ingredients)}
          addLabel="Add ingredient"
        />

        <ListEditor
          title="Method"
          hint="One step per box."
          items={form.steps}
          placeholder="Bring a large pan of salted water to the boil."
          onChange={(steps) => set('steps', steps)}
          addLabel="Add step"
          multiline
          numbered
        />

        {save.error && <ErrorBanner message={save.error} details={save.details} />}

        <div className="flex gap-2">
          <Button type="submit" disabled={save.pending}>
            {save.pending ? 'Saving…' : isEdit ? 'Save changes' : 'Create recipe'}
          </Button>
          <Button type="button" variant="ghost" onClick={() => navigate(-1)}>
            Cancel
          </Button>
        </div>
      </form>
    </div>
  )
}

/** Repeating rows of text, used for both ingredients and steps. */
function ListEditor({
  title,
  hint,
  items,
  placeholder,
  onChange,
  addLabel,
  multiline = false,
  numbered = false,
}: {
  title: string
  hint: string
  items: string[]
  placeholder: string
  onChange: (items: string[]) => void
  addLabel: string
  multiline?: boolean
  numbered?: boolean
}) {
  const update = (index: number, value: string) =>
    onChange(items.map((item, i) => (i === index ? value : item)))

  // Never remove the last row: an empty list leaves no way to type anything.
  const remove = (index: number) =>
    onChange(items.length === 1 ? [''] : items.filter((_, i) => i !== index))

  return (
    <section className="rounded-xl border border-ink-200 bg-white p-5">
      <h2 className="font-display text-xl font-semibold text-ink-900">{title}</h2>
      <p className="mt-0.5 text-xs text-ink-400">{hint}</p>

      <div className="mt-4 space-y-2">
        {items.map((item, index) => (
          <div key={index} className="flex items-start gap-2">
            {numbered && (
              <span className="mt-2 grid size-7 shrink-0 place-items-center rounded-full bg-brand-100 text-xs font-semibold text-brand-700">
                {index + 1}
              </span>
            )}

            {multiline ? (
              <textarea
                rows={2}
                value={item}
                placeholder={placeholder}
                onChange={(event) => update(index, event.target.value)}
                className={inputClass}
              />
            ) : (
              <input
                value={item}
                placeholder={placeholder}
                onChange={(event) => update(index, event.target.value)}
                onKeyDown={(event) => {
                  // Enter adds the next row instead of submitting the form —
                  // submitting halfway through a shopping list is never what
                  // was meant.
                  if (event.key === 'Enter') {
                    event.preventDefault()
                    onChange([...items.slice(0, index + 1), '', ...items.slice(index + 1)])
                  }
                }}
                className={inputClass}
              />
            )}

            <button
              type="button"
              onClick={() => remove(index)}
              aria-label={`Remove ${title.toLowerCase()} ${index + 1}`}
              className="mt-1 grid size-8 shrink-0 place-items-center rounded-lg text-ink-400 transition hover:bg-red-50 hover:text-red-600"
            >
              ×
            </button>
          </div>
        ))}
      </div>

      <Button
        type="button"
        variant="secondary"
        className="mt-3"
        onClick={() => onChange([...items, ''])}
      >
        + {addLabel}
      </Button>
    </section>
  )
}

function toRequest(form: FormState): CreateRecipeRequest {
  const lines = form.ingredients.map((line) => line.trim()).filter(Boolean)

  return {
    title: form.title.trim(),
    description: form.description.trim() || null,
    prepTimeMinutes: Number(form.prepTimeMinutes) || 0,
    cookTimeMinutes: Number(form.cookTimeMinutes) || 0,
    servings: Number(form.servings) || 1,
    difficulty: form.difficulty,
    imageUrl: form.imageUrl.trim() || null,
    cuisines: splitList(form.cuisines),
    tags: splitList(form.tags),
    // The same line goes in as both: `name` gets normalized into the canonical
    // ingredient for searching, `rawText` is preserved for display.
    ingredients: lines.map((line) => ({
      name: line,
      quantity: null,
      unit: null,
      rawText: line,
    })),
    steps: form.steps.map((step) => step.trim()).filter(Boolean),
  }
}

function splitList(value: string): string[] {
  return value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
}
