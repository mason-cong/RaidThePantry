/**
 * Mirrors the DTOs in RecipeApi/Application/Dtos. Hand-written rather than
 * generated: there are only a handful, and a generator would add a build step
 * plus a large file nobody reads.
 *
 * These are the wire contract. If a request starts failing in a way that looks
 * impossible, check this file against the C# records first.
 */

/** Serialized by name, not ordinal — see the JsonStringEnumConverter in Program.cs. */
export type Difficulty = 'Easy' | 'Medium' | 'Hard'

export const DIFFICULTIES: Difficulty[] = ['Easy', 'Medium', 'Hard']

export type RecipeSourceType = 'Manual' | 'UrlImport' | 'BulkScrape'

export interface RecipeSummary {
  id: string
  title: string
  imageUrl: string | null
  totalTimeMinutes: number
  difficulty: Difficulty
  cuisines: string[]
  /** Always false for anonymous callers; the API skips the subquery entirely then. */
  isFavorited: boolean
}

export interface RecipeIngredient {
  name: string
  quantity: number | null
  unit: string | null
  rawText: string | null
}

export interface RecipeDetail {
  id: string
  title: string
  description: string | null
  prepTimeMinutes: number
  cookTimeMinutes: number
  servings: number
  difficulty: Difficulty
  imageUrl: string | null
  sourceUrl: string | null
  sourceType: RecipeSourceType
  /**
   * True only when the signed-in caller created this recipe. Scraped recipes
   * have no owner and are editable by nobody. Drive the edit/delete controls
   * off this rather than comparing ids here — the server owns the rule.
   */
  isEditable: boolean
  isFavorited: boolean
  ingredients: RecipeIngredient[]
  steps: string[]
  cuisines: string[]
  tags: string[]
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasNextPage: boolean
}

export interface Cuisine {
  id: string
  name: string
  recipeCount: number
}

export interface Ingredient {
  id: string
  name: string
  category: string | null
  recipeCount: number
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
}

export interface CurrentUser {
  id: string
  email: string
  createdAt: string
}

/** The request body for both POST /api/recipes and PUT /api/recipes/{id}. */
export interface CreateRecipeRequest {
  title: string
  description: string | null
  prepTimeMinutes: number
  cookTimeMinutes: number
  servings: number
  difficulty: Difficulty
  imageUrl: string | null
  cuisines: string[]
  tags: string[]
  ingredients: CreateIngredient[]
  steps: string[]
}

export interface CreateIngredient {
  /** Free text as typed. The server normalizes it and keeps this as rawText. */
  name: string
  quantity: number | null
  unit: string | null
  rawText: string | null
}

export interface RecipeSearchParams {
  cuisines?: string[]
  tags?: string[]
  ingredients?: string[]
  exactIngredientMatch?: boolean
  difficulty?: Difficulty
  maxTotalTimeMinutes?: number
  search?: string
  page?: number
  pageSize?: number
  sortBy?: 'time' | 'title'
}
