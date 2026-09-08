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
