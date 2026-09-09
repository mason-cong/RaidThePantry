import { Button } from './Ui'

/**
 * Prev/next plus a page count. The API already computes totalPages and
 * hasNextPage on PagedResult, so there is no arithmetic to get wrong here.
 */
export function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
}: {
  page: number
  totalPages: number
  totalCount: number
  onPageChange: (page: number) => void
}) {
  if (totalPages <= 1) return null

  return (
    <nav className="flex items-center justify-between gap-4 pt-2" aria-label="Pagination">
      <Button variant="secondary" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
        ← Previous
      </Button>

      <p className="text-sm text-ink-500">
        Page <span className="font-medium text-ink-900">{page}</span> of {totalPages}
        <span className="hidden sm:inline"> · {totalCount} recipes</span>
      </p>

      <Button
        variant="secondary"
        onClick={() => onPageChange(page + 1)}
        disabled={page >= totalPages}
      >
        Next →
      </Button>
    </nav>
  )
}
