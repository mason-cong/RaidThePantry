import { http, toQuery } from './client'
import type {
  AuthResponse,
  CreateRecipeRequest,
  Cuisine,
  CurrentUser,
  Ingredient,
  PagedResult,
  RecipeDetail,
  RecipeSearchParams,
  RecipeSummary,
} from './types'

/** One function per endpoint. Nothing here holds state. */

export const searchRecipes = (params: RecipeSearchParams, signal?: AbortSignal) =>
  http.get<PagedResult<RecipeSummary>>(`/api/recipes${toQuery({ ...params })}`, signal)

export const getRecipe = (id: string, signal?: AbortSignal) =>
  http.get<RecipeDetail>(`/api/recipes/${id}`, signal)

/** Returns the new recipe's id. */
export const createRecipe = (recipe: CreateRecipeRequest) =>
  http.post<string>('/api/recipes', recipe)

export const updateRecipe = (id: string, recipe: CreateRecipeRequest) =>
  http.put<void>(`/api/recipes/${id}`, recipe)

export const deleteRecipe = (id: string) => http.delete<void>(`/api/recipes/${id}`)

export const getCuisines = (signal?: AbortSignal) =>
  http.get<Cuisine[]>('/api/cuisines', signal)

export const searchIngredients = (search: string, limit = 20, signal?: AbortSignal) =>
  http.get<Ingredient[]>(`/api/ingredients${toQuery({ search, limit })}`, signal)

export const getFavorites = (page = 1, pageSize = 20, signal?: AbortSignal) =>
  http.get<PagedResult<RecipeSummary>>(`/api/favorites${toQuery({ page, pageSize })}`, signal)

export const addFavorite = (recipeId: string) =>
  http.post<void>(`/api/favorites/${recipeId}`)

export const removeFavorite = (recipeId: string) =>
  http.delete<void>(`/api/favorites/${recipeId}`)

export const register = (email: string, password: string) =>
  http.post<AuthResponse>('/api/auth/register', { email, password })

export const login = (email: string, password: string) =>
  http.post<AuthResponse>('/api/auth/login', { email, password })

export const getCurrentUser = (signal?: AbortSignal) =>
  http.get<CurrentUser>('/api/auth/me', signal)

/** Returns the new recipe's id. A duplicate URL throws ApiError with status 409. */
export const importFromUrl = (url: string) => http.post<string>('/api/import/url', { url })

/**
 * The 409 body carries the id of the recipe that already exists, so a repeat
 * import can navigate there instead of showing an error. Shape defined in
 * ImportController.
 */
export function duplicateRecipeId(body: unknown): string | null {
  if (!body || typeof body !== 'object') return null
  const id = (body as { recipeId?: unknown }).recipeId
  return typeof id === 'string' ? id : null
}
