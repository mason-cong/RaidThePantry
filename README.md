# RecipeFinder

Recipe search API — filter by cuisine, ingredients, difficulty and time; import
recipes from external URLs; bulk-scrape into a staging table. Browsing is fully
anonymous; an account is only needed to contribute recipes or save favorites.

- `RecipeApi/` — ASP.NET Core Web API (.NET 10, controllers, EF Core, Postgres)
- `RecipeApi.Worker/` — console host for the bulk `scrape` and `promote` commands
- `web/` — React frontend (Vite, TypeScript, Tailwind CSS v4)
- `Dockerfile` — builds both halves into one image; see [Production](#production)

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

## Frontend (`web/`)

React 19 + TypeScript + Tailwind v4, built with Vite. State is plain React —
`useState` and `useEffect` behind two hooks in `src/hooks/useApi.ts` — with no
data-fetching library. At this size a query cache would be more machinery than
the problem needs.

```bash
cd web
npm install
npm run build && npm run preview     # http://localhost:4173
```

**Read this before running `npm run dev`.** The dev server does not work from
this checkout's path. Vite converts file paths to URLs, and the `#` in the
parent `C#` directory is read as the start of a URL fragment, so module
resolution truncates to `.../C` and every import fails. Vite prints a warning
about it at startup, then serves `main.tsx` untransformed — the page loads and
does nothing, with the real error only in the terminal.

`npm run build` is unaffected, so `npm run preview` runs the app correctly
against the same API proxy. The tradeoff is no hot reload: each change needs a
rebuild. The only actual fix is a checkout path with no `#` in it — renaming the
`C#` folder to `CSharp` restores `npm run dev` — which is a change to your
folder layout, so it is left as your call rather than made here.

Either server proxies `/api` to `http://localhost:5282`, so the browser sees one
origin and CORS never comes up. That holds in production too — the image serves
the SPA from the API's own `wwwroot` — which is why CORS is off unless
`Cors:AllowedOrigins` is explicitly set. See [Production](#production).

What is where:

| Path | What it holds |
|---|---|
| `src/api/types.ts` | hand-written mirrors of the C# DTOs — the wire contract |
| `src/api/client.ts` | the only code that calls `fetch`; token handling and `ApiError` |
| `src/hooks/useApi.ts` | `useApi` (load and hold), `useAction` (submit), `useDebounced` |
| `src/auth/` | the signed-in account; token in `localStorage` |
| `src/pages/` | one file per screen |

Two decisions worth knowing:

**Search filters live in the URL, not in component state.** That is what makes a
filtered search linkable and the back button work; `useState` looks simpler
until someone shares a search and the recipient gets the unfiltered list.

**The token is kept in `localStorage`.** It survives a reload, and anything that
can run script on this origin can read it. That is the right trade here — there
is no refresh token, so in-memory storage would mean signing in again on every
reload — but it is a trade, and it is why the app must never render unsanitised
HTML from a recipe.

`isEditable` and `isFavorited` come from the server on every recipe DTO, so the
frontend never reimplements the ownership rule and a list draws its own hearts
without a request per card.

Enums cross the wire as names — `"difficulty": "Easy"`, not `0`. The
`JsonStringEnumConverter` in `Program.cs` and the matching one in the test
suite's `Api.Json` have to agree; change one and every response carrying a
`Difficulty` stops deserializing in the tests.

## Tests

```bash
docker compose up -d          # the suite needs a live PostgreSQL
dotnet test
```

168 tests, about 35 seconds. They boot the real application in-process with
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
| `RateLimiterTests` | that the limiter bites, sends `Retry-After`, and leaves reads alone |
| `Unit/GuardedConnectTests` | the SSRF rule at the connect callback, independent of the pre-flight check |
| `Unit/` | `IngredientNormalizer` and `IsoDurationParser` directly — fast and precise |

`RateLimiterTests` boots its own host with a real (tiny) limit. The shared
fixture runs with the limiter effectively disabled, because every other test
registers an account from loopback — one rate-limit partition — and production
limits would throttle the suite rather than the code.

`ImportTests` starts a real HTTP server on loopback rather than stubbing
`HttpClient`, because redirect following, the size ceiling and the per-hop host
check are the parts worth testing.

## Production

One image serves both halves: the API hosts the built SPA out of `wwwroot`, so
the browser sees a single origin. CORS never comes into play, and the frontend
needs no API base URL — `fetch('/api/...')` is already correct.

```bash
export DOMAIN=recipes.example.com
export JWT_SIGNING_KEY="$(openssl rand -base64 32)"
mkdir -p certs   # a Cloudflare Origin CA cert goes here — see below

docker compose -f docker-compose.prod.yml build
docker compose -f docker-compose.prod.yml run --rm api --migrate   # deploy step
docker compose -f docker-compose.prod.yml up -d
# https://<DOMAIN>, once DNS points at this host
```

That compose file is a **local rehearsal**, not a production topology — it runs
the real image with Production settings, plus Caddy in front doing what
Cloudflare's origin connection expects, so the startup checks, headers, SPA
hosting and TLS handshake can all be exercised before any of it reaches a
server. A real deployment still wants a managed database in place of the
`postgres` service here.

**Migrations are a deliberate step, not something that happens on boot.** The
image's entrypoint never migrates; `--migrate` (like `--seed`) runs and exits.
That way a failed migration cannot crash-loop the app, and two instances
starting together cannot race each other.

### Deploying on a bare VM behind Cloudflare (OCI, or similar)

This is the arrangement `docker-compose.prod.yml` and the root `Caddyfile`
actually set up, and the one verified against the running containers below.

```
browser --TLS--> Cloudflare --TLS--> Caddy --plain HTTP--> api (loopback only)
                                       |
                                   Origin CA cert
```

**One hop, deliberately.** With no OCI Load Balancer in front of the VM, this
is the only proxy the app sees, so `Hosting:BehindReverseProxy`'s default
`ForwardLimit` of 1 is already correct — no extra configuration needed for a
second hop. Adding an OCI Load Balancer later would change that.

**What makes trusting a forwarded header safe at all.** `Hosting:BehindReverseProxy=true`
trusts `X-Forwarded-For` from anyone who connects — that is only sound because
the OCI Security List admits *only* Cloudflare's published IP ranges
(`https://www.cloudflare.com/ips/`) on 80/443. Restrict SSH to your own address
while you're in there. Without that lockdown, someone who finds the VM's IP
could bypass Cloudflare entirely and forge the header the rate limiter reads.

**TLS between Cloudflare and the VM.** Set Cloudflare's SSL/TLS mode to **Full
(strict)** — Flexible means the Cloudflare-to-origin hop is plain HTTP even
though the browser shows a padlock. Full (strict) needs the origin to present a
certificate Cloudflare actually trusts, and a Cloudflare **Origin CA**
certificate is the free, made-for-this-exact-purpose answer: generate one at
*SSL/TLS → Origin Server → Create Certificate*, save the two halves as
`certs/origin.pem` and `certs/origin.key` next to the Caddyfile (gitignored;
they never belong in the image or the repo), and Caddy picks them up. Because
Security Lists already restrict inbound traffic to Cloudflare's ranges, this
certificate is by construction only ever presented to Cloudflare — a
certificate meant for exactly one audience serving exactly that audience.

**The api container publishes nothing to the public interface.** It binds
`127.0.0.1:8080` — reachable for a health check over SSH, invisible from the
internet — and Caddy reaches it over the compose network by service name.
Caddy is the only container with a published port, and it is also the only
thing the Security List admits traffic to.

Verified locally by standing the full stack up with a throwaway self-signed
certificate in place of a real Origin CA one: the api port confirmed bound to
loopback only (not `0.0.0.0`), Caddy served the right certificate over 443 and
proxied through to a working `/health`, plain HTTP on `:80` redirected to
HTTPS, and — the one worth spelling out — a forged `X-Forwarded-For` sent
straight at Caddy did **not** get a fresh rate-limit allowance. Caddy appends
its own real peer address to whatever arrives, and `ForwardLimit=1` reads the
last entry, so the app used Caddy's value rather than the forged one. An
attacker rotating a fake header to dodge the auth limit does not work here.

### Configuration

Environment variables use `__` where the key has a `:`.

| Variable | Required | Notes |
|---|---|---|
| `ConnectionStrings__Postgres` | yes | Startup fails without it |
| `Jwt__SigningKey` | yes | 32+ bytes. Never in a settings file |
| `Scraping__UserAgent` | yes | Startup fails while it's still the built-in default |
| `Hosting__BehindReverseProxy` | if proxied | See below |
| `Cors__AllowedOrigins__0` | only if split | Unset means CORS is off entirely |
| `RateLimiting__AuthPerMinute` | no | Default 30, per address |
| `RateLimiting__ImportPerMinute` | no | Default 10, per account |

**The app refuses to start rather than starting wrong.** Outside Development it
rejects a missing connection string, a missing or too-short signing key, the
placeholder user-agent, and — the one that matters — `AllowLoopbackHosts` left
on, which would re-open the import endpoint onto everything else on the host.
Every one of those fails silently otherwise: the app comes up and serves traffic
while being quietly insecure or quietly broken.

**`Hosting__BehindReverseProxy` defaults to false and must be turned on
deliberately.** With it on, `X-Forwarded-*` is trusted from anyone, which is
correct behind a proxy that overwrites those headers and a way to spoof your
address past the per-IP rate limits if nothing does. The failure mode of leaving
it off when you should have turned it on is visible (wrong client IPs in logs);
the failure mode of the reverse is silent, so the default is the safe one.

TLS termination is the platform's job. There is deliberately no
`UseHttpsRedirection` — at the app layer, behind a proxy, that is the classic way
to build a redirect loop. HSTS is sent in Production regardless.

### What is enforced

**SSRF.** `GuardedConnect` resolves the host and opens the socket in one step,
so the address that was vetted is the address that gets dialled. This is what
closes DNS rebinding: `FetchableUrl`'s pre-flight check exists for the error
message, and on its own it loses to a name that answers differently the second
time it is resolved. Both share one copy of the address rules so they cannot
drift apart.

**Rate limits.** Import is capped per account, because each call spends an
outbound request against somebody else's site. Auth is capped per address, which
is the right tool against credential stuffing and the wrong one against someone
guessing a single password — they can rotate addresses, which is what the
10-character minimum is for. Reads are deliberately uncapped: browsing is the
anonymous default path through this app, and limiting it would be a
self-inflicted outage the first time a link got shared. A 429 carries
`Retry-After` and a `problem+json` body.

**Headers.** `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, and a CSP
whose `script-src` has no `'unsafe-inline'` — that is the directive that matters
here, because an XSS on this origin can read the auth token out of
`localStorage`. `img-src` allows any https origin, because recipe images are
hotlinked from wherever a recipe was imported from.

**Caching.** `/assets/*` is fingerprinted by Vite and served `immutable`; the SPA
shell is always `no-cache`, or a deploy leaves browsers running the previous
bundle against the new API. The shell rule keys off the response content type
rather than the path, because it goes out by three different routes and only one
of them passes through `StaticFileOptions`.

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

DNS rebinding is closed by `GuardedConnect`, the handler's connect callback: it
resolves the host and opens the socket itself, so the address that was vetted is
the address that gets dialled. `FetchableUrl` remains the pre-flight check — it
exists to produce a good error message before anything is dialled — and on its
own it would lose to a name that answers differently the second time it is
resolved. Both apply the same `IsAddressAllowed` so they cannot drift.

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
