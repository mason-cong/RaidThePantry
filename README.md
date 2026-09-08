# RecipeFinder

Recipe search API — filter by cuisine, ingredients, difficulty and time; import
recipes from external URLs; bulk-scrape into a staging table. Browsing is fully
anonymous; an account is only needed to contribute recipes or save favorites.

- `RecipeApi/` — ASP.NET Core Web API (.NET 10, controllers, EF Core, Postgres)
- `RecipeApi.Worker/` — console host for the bulk `scrape` and `promote` commands

## Local setup

**1. Start Postgres**

```bash
docker compose up -d
```

Mapped to host port **5433**, not 5432 — a local PostgreSQL service may already
hold `127.0.0.1:5432` and will win whenever a client resolves `localhost`,
producing a confusing `28P01 password authentication failed` against a container
that is actually healthy.

**2. Create your local settings**

```bash
cp RecipeApi/appsettings.Development.example.json RecipeApi/appsettings.Development.json
```

`appsettings.Development.json` is gitignored. Anything genuinely secret — the JWT
signing key above all — goes in user-secrets, never in a settings file:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<32+ random bytes, base64>" --project RecipeApi
```

**3. Apply migrations**

```bash
dotnet tool install --global dotnet-ef      # first time only
dotnet ef database update -p RecipeApi -s RecipeApi
```

**4. Run**

```bash
dotnet run --project RecipeApi --launch-profile http
```

- API: http://localhost:5282
- Swagger: http://localhost:5282/swagger
- Health: http://localhost:5282/health → `{"status":"ok","database":"connected"}`

## Tests

```bash
docker compose up -d          # the suite needs a live PostgreSQL
dotnet test
```

157 tests, about 30 seconds. They boot the real application in-process with
`WebApplicationFactory` and run against a real database — nothing is
substituted for a fake. That is deliberate: the defects this suite exists to
catch are EF translation failures, LIKE escaping, index behaviour, unique
constraints and cascades, and every one of them would pass against an in-memory
provider.

The suite uses its own database, **`recipefinder_test`**, created on first run.
Your development data is never touched. Point `RECIPEFINDER_TEST_POSTGRES` at
another server to override the connection.

Test classes share one application and run sequentially, re-seeding to the known
18-recipe fixture before each class, so no test depends on what ran before it.

What is covered:

| Area | What it pins down |
|---|---|
| `GuestAccessTests` | every read endpoint serves anonymous callers |
| `SearchTests` | the four search defects: AND-of-EXISTS keywords, LIKE escaping, sort tiebreakers, page clamping |
| `AuthTests` | registration, login, and that login is not an account-existence oracle |
| `RecipeWriteTests` | creator-only ownership, 403 vs 404, ingredient normalization and reuse |
| `FavoritesTests` | idempotent save/unsave, per-account isolation, cascade on recipe delete |
| `ImportTests` | JSON-LD shapes, and the SSRF guard including redirect-to-metadata |
| `Unit/` | `IngredientNormalizer` and `IsoDurationParser` directly — fast and precise |

`ImportTests` starts a real HTTP server on loopback rather than stubbing
`HttpClient`, because redirect following, the size ceiling and the per-hop host
check are the parts worth testing.

## Accounts

Browsing needs no account. `POST /api/auth/register` and `/api/auth/login` both
return a bearer token; `GET /api/auth/me` reports the signed-in account and is
the cheap way for a frontend to check whether its token is still valid.

Passwords require 10 characters and nothing else — no uppercase/digit/symbol
rules. That follows NIST SP 800-63B: composition rules mostly yield `Password1!`,
which is predictable to an attacker and irritating to everyone else, while a
longer passphrase is both stronger and easier to remember. The rule lives in
`AuthServiceCollectionExtensions` and is mirrored by `MinLength` on
`RegisterRequest` — change both together or the two validators disagree.

In Development, a missing `Jwt:SigningKey` falls back to a random ephemeral key
with a warning, so a fresh clone runs without configuration. Tokens then stop
working across restarts. Outside Development a missing key is a startup error.

## Importing recipes

`POST /api/import/url` fetches a page, pulls a schema.org/Recipe out of its
JSON-LD, and saves it synchronously — the caller gets the recipe back. It needs
an account, because it makes the server fetch a URL the caller chose.

Duplicate imports of the same URL return **409** carrying the existing recipe id
rather than an error, so a repeated click lands somewhere useful.

`FetchableUrl` is the SSRF guard: http/https only, no embedded credentials, and
every resolved address checked against loopback, private, link-local (including
`169.254.169.254` cloud metadata), CGNAT and unique-local ranges. Redirects are
followed by hand so **each hop** is re-checked — automatic redirects would let a
public URL bounce to an internal one after the check had already passed.

Its known gap is DNS rebinding: the guard resolves a name to vet it, and the
HTTP client resolves again to connect. Closing that needs the connection pinned
to the vetted address, and is worth doing before this runs anywhere public.

`Scraping:AllowLoopbackHosts` is **true** in `appsettings.Development.json` so
the importer can be tested against `fixtures/fixture_server.py`:

```bash
python fixtures/fixture_server.py 8099
# then POST http://127.0.0.1:8099/simple, /graph, /sections, /blob, ...
```

That flag must stay false anywhere reachable. The fixture server also serves the
failure cases — redirect-to-metadata, oversized pages, non-HTML responses, and
pages with malformed or absent JSON-LD.

## Favorites

`GET/POST/DELETE /api/favorites` is the only wholly auth-gated controller.
`POST /api/favorites/{recipeId}` saves, `DELETE` unsaves, and the list comes
back most recently saved first.

Both writes are idempotent: saving something already saved is 204 rather than a
conflict, and unsaving something that was never saved is 204 rather than a 404.
The caller asked for a state, and in both cases that state holds afterwards.
Only an unknown recipe id is a 404.

`RecipeSummaryDto` and `RecipeDetailDto` both carry `isFavorited`, so a list
view can draw its own heart state without a round trip per card. It is always
false for anonymous callers, and search skips the subquery entirely in that
case rather than running one that can only return false.

Deleting a recipe removes it from everyone's favorites by database cascade.

## Bulk scraping (RecipeApi.Worker)

Two commands, deliberately separate, because fetching is the expensive
rate-limited half and transforming is the cheap half that keeps changing.

```bash
dotnet run --project RecipeApi.Worker -- scrape urls.txt [--refetch]
dotnet run --project RecipeApi.Worker -- promote [--repromote]
dotnet run --project RecipeApi.Worker -- status
```

**`scrape`** fetches each URL into `staging.ScrapedPages` and never touches
`Recipes`. It honors `robots.txt` — a named group for `RecipeFinderBot` beats
the wildcard group, longest-matching rule wins, and `Crawl-delay` overrides the
politeness default when a site asks for more. Pages where extraction succeeds
store just the recipe's JSON-LD node (a few KB); pages where it fails store the
whole HTML, because those are exactly the corpus `HtmlFallbackParser` needs.
Re-running skips URLs already staged unless `--refetch` is passed.

**`promote`** turns staged pages into recipes with `SourceType = BulkScrape` and
**no owner**, which is what makes them read-only through the API. It makes no
network calls, so it is fast and freely re-runnable over the whole corpus.

After changing the normalizer or mapper, raise `Scraping:ParserVersion` and run
`promote --repromote`. Rows promoted under an older version are re-transformed
from the staged JSON — **no re-crawl**. Re-promotion updates each recipe in
place rather than delete-and-recreate, so ids stay stable and favorites survive.

Politeness is not permission: check a site's terms before pointing the crawler
at it. To test against the fixture server instead:

```bash
python fixtures/fixture_server.py 8099
Scraping__AllowLoopbackHosts=true dotnet run --project RecipeApi.Worker -- scrape urls.txt
```

## Database notes

The initial migration contains hand-written SQL alongside the EF-generated
tables: `CREATE EXTENSION pg_trgm` plus GIN trigram indexes on
`Ingredients.Name` and `Recipes.Title`. Those are what make the `ILIKE '%term%'`
searches usable — a B-tree index cannot serve a predicate with a leading
wildcard. Don't regenerate `InitialCreate` without carrying that SQL forward.

`staging.ScrapedPages` is the Worker's crawl landing zone. The API never reads it.

To reset completely (this destroys the volume):

```bash
docker compose down -v && docker compose up -d
dotnet ef database update -p RecipeApi -s RecipeApi
```
