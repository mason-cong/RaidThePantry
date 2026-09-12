# RecipeFinder

Recipe search API — filter by cuisine, ingredients, difficulty and time; import
recipes from external URLs; bulk-scrape into a staging table. Browsing is fully
anonymous; an account is only needed to contribute recipes or save favorites.

- `RecipeApi/` — ASP.NET Core Web API (.NET 10, controllers, EF Core, Postgres)
- `RecipeApi.Worker/` — console host for the bulk `discover`, `scrape` and `promote` commands
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

**The landing page is a prompt, not a listing.** With no query and no filters it
makes no search request at all and shows a starting point instead. Listing
everything was harmless at 18 recipes and stops being so the moment the crawler
runs — paging through thousands nobody asked about is not browsing. Filtering
and paging were always done in SQL, so the browser never received the whole
catalogue; this is about what is worth putting in front of someone, not about
where the work happens.

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

255 tests, about 50 seconds. They boot the real application in-process with
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
| `ScrapeJobTests` | the crawl: robots.txt, what is stored per outcome, failures mid-batch, refetch |
| `PromoteJobTests` | staging → recipes, and that a re-promote updates in place instead of replacing |
| `Unit/RobotsTxtTests` | the robots.txt rules — group precedence, longest match, wildcards, `$`, Crawl-delay |
| `Unit/SitemapDiscoveryTests` | sitemap-index recursion, `--match`, the `--limit` cap, off-site and disallowed URLs |
| `Unit/CuisineNameTests` | collapsing "American", "American Cuisine" and "American (US) Cuisine" into one |
| `Unit/TagNameTests` | dropping CMS metadata that publishers put in schema.org `keywords` |
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
export DOMAIN=raidthepantry.ca
export ACME_EMAIL=you@example.com
export JWT_SIGNING_KEY="$(openssl rand -base64 32)"

docker compose -f docker-compose.prod.yml build
docker compose -f docker-compose.prod.yml run --rm api --migrate   # deploy step
docker compose -f docker-compose.prod.yml up -d
# https://<DOMAIN>, once DNS points at this host
```

That same file is what runs in production on a single VM. Locally it is a
rehearsal — Caddy cannot obtain a certificate for a name that does not resolve
publicly, so test against `http://127.0.0.1:8080` instead, which the api
publishes on loopback for exactly that. A larger deployment would want a
managed database in place of the `postgres` service.

**Migrations are a deliberate step, not something that happens on boot.** The
image's entrypoint never migrates; `--migrate` (like `--seed`) runs and exits.
That way a failed migration cannot crash-loop the app, and two instances
starting together cannot race each other.

### Deploying on a bare VM (OCI, or similar)

What `docker-compose.prod.yml` and the root `Caddyfile` actually set up:

```
browser --TLS--> Caddy --plain HTTP--> api (loopback only) --> postgres
                   |
            Let's Encrypt cert,
            obtained and renewed
            by Caddy itself
```

**TLS is automatic.** There is no certificate on disk and no `tls` directive:
Caddy provisions one from Let's Encrypt on first request and renews it. Two
things that depends on, both easy to get wrong:

- **DNS must resolve before the stack starts.** Let's Encrypt validates by
  connecting back on port 80, so a name that does not resolve yet fails
  issuance — and repeated failures count against a rate limit that locks you
  out for hours. Point the A record, confirm it resolves, *then* bring the
  stack up.
- **Port 80 must stay open to the world.** It is not just the redirect; it is
  how the HTTP-01 challenge reaches Caddy. Restricting it breaks renewal
  silently, about sixty days later.

**The `caddy-data` volume is load-bearing.** It holds the ACME account key and
every issued certificate. `docker compose down` keeps it; `down -v` does not,
and re-requesting from scratch a few times in a week hits the duplicate-
certificate rate limit.

**One hop, deliberately.** Caddy is the only proxy, so `ForwardLimit` of 1 is
correct and `Hosting:BehindReverseProxy=true` reads the address Caddy appends.
Putting a CDN or an OCI Load Balancer in front would add a hop and change that.

**The api container publishes nothing publicly.** It binds `127.0.0.1:8080` —
reachable over SSH for a health check, invisible from the internet — and Caddy
reaches it over the compose network by service name. Caddy is the only
container with a published port.

**Two firewalls, not one.** The OCI Security List and the VM's own
firewalld/iptables are independent, and the stock images block everything but
SSH at the OS level regardless of what the Security List says. Open 80 and 443
in both, and restrict 22 to your own address.

Verified against the running stack: the api port bound to loopback only (not
`0.0.0.0`), Caddy serving over 443 and proxying through to a working `/health`,
plain HTTP redirecting to HTTPS, and — the one worth spelling out — a forged
`X-Forwarded-For` sent straight at Caddy did **not** get a fresh rate-limit
allowance. Caddy appends its own real peer address to whatever arrives, and
`ForwardLimit=1` reads the last entry, so the app used Caddy's value rather
than the forged one. An attacker rotating a fake header to dodge the auth limit
does not work here.

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

Every client that fetches a caller-supplied URL is built by
`GuardedHttpHandler` — the API's importer, the Worker's page fetcher, and the
Worker's robots.txt client. That helper exists because the alternative already
failed: when the connect guard was first added, only the API was switched over,
and the bulk crawler — which fetches far more URLs than the API ever will —
quietly kept an unguarded handler. One shared constructor makes that particular
mistake impossible rather than merely unlikely.

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

Three commands, deliberately separate, because finding pages, fetching them and
transforming them fail differently and change at different rates. Each writes
something the next one reads, and each can be re-run on its own.

**`discover`** reads the site's own sitemaps — declared in robots.txt — and
writes a list of candidate URLs. It never touches the database.

```bash
dotnet run --project RecipeApi.Worker -- \
  discover https://www.example.com --match=/recipe/ --limit 50 --out crawl/urls.txt
```

Sitemaps rather than link-following: a sitemap is the publisher stating what it
wants indexed, in a format meant for exactly this, and it avoids guessing which
links on a page are recipes. `--limit` defaults to **100 and is the point** —
real recipe sitemaps are enormous (allrecipes.com lists over 13,000 recipe URLs
in one of its four child sitemaps), so an uncapped run would hand `scrape` days
of requests against someone else's origin. `--limit 0` lifts the cap and has to
be typed deliberately. URLs that robots.txt disallows are dropped here, before
the crawl ever sees them.

```bash
dotnet run --project RecipeApi.Worker -- scrape urls.txt [--refetch]
dotnet run --project RecipeApi.Worker -- promote [--repromote]
dotnet run --project RecipeApi.Worker -- status
```

In production the Worker ships inside the same image as the API, under its own
entry point, and the compose file exposes it as a `worker` service. Put the URL
list in `crawl/` — the directory is bind-mounted, rather than the file, because
bind-mounting a file that does not exist yet makes Docker silently create a
*directory* in its place:

```bash
mkdir -p crawl && cp urls.txt crawl/
docker compose -f docker-compose.prod.yml run --rm worker scrape /crawl/urls.txt
docker compose -f docker-compose.prod.yml run --rm worker promote
docker compose -f docker-compose.prod.yml run --rm worker status
```

It sits behind a compose profile, so `up` never starts it: these are one-shot
commands, and a crawler that began fetching the moment the stack came up is not
what anyone wants.

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

### What real sites actually do

Tried against four large recipe sites, 5 URLs each. `discover` worked on all
four. `scrape` did not:

| Site | Result |
|---|---|
| delish.com | fetched fine; 2 of 5 were recipes, 3 were roundup pages |
| allrecipes.com | **403** on every recipe page |
| seriouseats.com | **403** |
| simplyrecipes.com | **403** |

The 403s are not robots.txt — the wildcard group permits those paths — and not
the headers. `curl` fetches the identical URL with the identical User-Agent and
Accept over the same HTTP version and gets 200, while .NET gets 403. That leaves
the client fingerprint itself, which is bot management at the CDN. Getting past
it would mean impersonating a browser's TLS signature, which is circumventing an
access control the publisher deliberately put up, so this crawler does not and
will not do it. Those three sites are simply not scrapable by this tool.

Politeness is not permission, and neither is a permissive robots.txt: all four of
those sites explicitly name and block AI and scraper bots elsewhere in the same
file. Check a site's terms before pointing the crawler at it, and note that
crawling and republishing are different questions — this app *serves* what it
stores.

Real pages also broke the normalizer in ways the fixtures never could: `4 c.
cold heavy cream` normalized to `c heavy cream`, because the single-letter cup
abbreviation was not in the units list, and `confectioners’ sugar` with a curly
apostrophe produced a different canonical name than the straight-quoted form.
Both are fixed, and both were found by running it rather than by reading it.

To test against the fixture server instead:

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
