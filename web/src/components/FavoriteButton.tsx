import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { addFavorite, removeFavorite } from '../api/recipes'
import { useAuth } from '../auth/useAuth'

/**
 * Toggles a recipe's saved state.
 *
 * The update is optimistic — the heart fills the moment it is clicked and only
 * reverts if the server disagrees. Both API calls are idempotent (saving twice
 * is 204, not a conflict), so a double click cannot get the two out of step.
 */
export function FavoriteButton({
  recipeId,
  isFavorited,
  onChange,
  size = 'md',
}: {
  recipeId: string
  isFavorited: boolean
  /** Lets a list owner keep its own copy of the recipe in step. */
  onChange?: (favorited: boolean) => void
  size?: 'sm' | 'md'
}) {
  const { user } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [saved, setSaved] = useState(isFavorited)
  const [pending, setPending] = useState(false)

  // A fresh search replaces the summaries under us, so the prop is the source
  // of truth whenever it changes.
  useEffect(() => setSaved(isFavorited), [isFavorited])

  const toggle = async (event: React.MouseEvent) => {
    // The button usually sits inside a card that is itself a link.
    event.preventDefault()
    event.stopPropagation()

    if (!user) {
      // Come back here after signing in rather than dumping them on the home page.
      navigate('/login', { state: { from: location.pathname + location.search } })
      return
    }

    const next = !saved
    setSaved(next)
    setPending(true)

    try {
      await (next ? addFavorite(recipeId) : removeFavorite(recipeId))
      onChange?.(next)
    } catch {
      setSaved(!next) // put the heart back; the server is the authority
    } finally {
      setPending(false)
    }
  }

  return (
    <button
      type="button"
      onClick={toggle}
      disabled={pending}
      aria-pressed={saved}
      aria-label={saved ? 'Remove from favorites' : 'Save to favorites'}
      title={user ? undefined : 'Sign in to save recipes'}
      className={`grid place-items-center rounded-full backdrop-blur transition
        ${size === 'sm' ? 'size-8 text-base' : 'size-10 text-lg'}
        ${saved ? 'bg-brand-500 text-white' : 'bg-white/90 text-ink-400 hover:text-brand-500'}
        disabled:opacity-60`}
    >
      <span aria-hidden="true">{saved ? '♥' : '♡'}</span>
    </button>
  )
}
