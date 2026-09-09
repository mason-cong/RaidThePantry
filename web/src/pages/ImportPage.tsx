import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { duplicateRecipeId, importFromUrl } from '../api/recipes'
import { Button, ErrorBanner, Field, inputClass } from '../components/Ui'

/**
 * Import by URL.
 *
 * The interesting case is 409: the URL was imported before, and the response
 * carries the id of the recipe that already exists. Navigating there is what
 * the caller wanted anyway, so a repeat import is a shortcut rather than an
 * error — that is exactly why the API returns the id with the conflict.
 */
export function ImportPage() {
  const navigate = useNavigate()

  const [url, setUrl] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    setPending(true)
    setError(null)

    try {
      const id = await importFromUrl(url.trim())
      navigate(`/recipes/${id}`)
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 409) {
        const existingId = duplicateRecipeId(cause.body)
        if (existingId) {
          navigate(`/recipes/${existingId}`)
          return
        }
      }
      setError(cause instanceof ApiError ? explain(cause) : 'Something went wrong.')
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mx-auto max-w-2xl">
      <h1 className="font-display text-3xl font-bold text-ink-900">Import from a URL</h1>
      <p className="mt-2 text-ink-500">
        Paste a link to a recipe page. If the page publishes structured recipe data, it will be
        pulled in — ingredients, steps, times and all.
      </p>

      <form onSubmit={submit} className="mt-6 space-y-4">
        <Field label="Recipe URL" hint="Must be a public http or https address.">
          <input
            type="url"
            required
            value={url}
            onChange={(event) => setUrl(event.target.value)}
            placeholder="https://example.com/recipes/carbonara"
            className={inputClass}
          />
        </Field>

        {error && <ErrorBanner message={error} />}

        <Button type="submit" disabled={pending || url.trim().length === 0}>
          {pending ? 'Fetching the page…' : 'Import recipe'}
        </Button>
      </form>

      <div className="mt-10 rounded-xl border border-ink-200 bg-white p-5 text-sm text-ink-500">
        <h2 className="font-medium text-ink-900">If it doesn't work</h2>
        <ul className="mt-2 list-inside list-disc space-y-1">
          <li>Not every site publishes recipe data — blogs often don't.</li>
          <li>Link to the recipe page itself, not a category or search page.</li>
          <li>Private or internal addresses are refused on purpose.</li>
        </ul>
      </div>
    </div>
  )
}

/** The API's own message is the useful one; these add what to do about it. */
function explain(error: ApiError): string {
  switch (error.status) {
    case 422:
      return `${error.message} The page loaded, but there was no recipe data on it.`
    case 502:
      return `${error.message} That is the other site failing, not this one — worth retrying later.`
    default:
      return error.message
  }
}
