import type { ReactNode } from 'react'

/** Small presentational pieces used across pages. No data fetching here. */

export function Spinner({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-3 py-12 text-ink-500" role="status">
      <span className="size-5 animate-spin rounded-full border-2 border-ink-300 border-t-brand-500" />
      <span className="text-sm">{label}…</span>
    </div>
  )
}

export function ErrorBanner({
  message,
  details = [],
  onRetry,
}: {
  message: string
  details?: string[]
  onRetry?: () => void
}) {
  return (
    <div
      // role="alert" so a screen reader announces a failure that appears after
      // the page has already rendered.
      role="alert"
      className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800"
    >
      <p className="font-medium">{message}</p>

      {details.length > 0 && (
        <ul className="mt-2 list-inside list-disc space-y-0.5 text-red-700">
          {details.map((detail) => (
            <li key={detail}>{detail}</li>
          ))}
        </ul>
      )}

      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="mt-3 rounded-md bg-red-100 px-3 py-1.5 font-medium text-red-900 transition hover:bg-red-200"
        >
          Try again
        </button>
      )}
    </div>
  )
}

export function EmptyState({
  title,
  children,
  icon = '🍳',
}: {
  title: string
  children?: ReactNode
  icon?: string
}) {
  return (
    <div className="rounded-xl border border-dashed border-ink-300 bg-white px-6 py-16 text-center">
      <p className="text-4xl" aria-hidden="true">
        {icon}
      </p>
      <h2 className="mt-4 text-lg font-semibold text-ink-900">{title}</h2>
      {children && <div className="mt-2 text-sm text-ink-500">{children}</div>}
    </div>
  )
}

const BUTTON_VARIANTS = {
  primary: 'bg-brand-500 text-white hover:bg-brand-600 shadow-sm',
  secondary: 'bg-white text-ink-700 border border-ink-300 hover:bg-ink-50',
  danger: 'bg-red-600 text-white hover:bg-red-700 shadow-sm',
  ghost: 'text-ink-500 hover:bg-ink-100 hover:text-ink-900',
} as const

export function Button({
  variant = 'primary',
  className = '',
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: keyof typeof BUTTON_VARIANTS
}) {
  return (
    <button
      {...props}
      className={`inline-flex items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-medium transition
        disabled:cursor-not-allowed disabled:opacity-50 ${BUTTON_VARIANTS[variant]} ${className}`}
    />
  )
}

export function Field({
  label,
  hint,
  children,
}: {
  label: string
  hint?: string
  children: ReactNode
}) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-sm font-medium text-ink-700">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-xs text-ink-400">{hint}</span>}
    </label>
  )
}

/** One input style, so every form looks like the same application. */
export const inputClass =
  'w-full rounded-lg border border-ink-300 bg-white px-3 py-2 text-sm text-ink-900 ' +
  'placeholder:text-ink-400 focus:border-brand-500 focus:outline-none'

export function Badge({ children }: { children: ReactNode }) {
  return (
    <span className="rounded-full bg-brand-100 px-2.5 py-0.5 text-xs font-medium text-brand-700">
      {children}
    </span>
  )
}

/** "1 h 25 min" reads better than "85 min" once times get long. */
export function formatMinutes(minutes: number): string {
  if (minutes <= 0) return '—'
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  if (hours === 0) return `${rest} min`
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`
}
