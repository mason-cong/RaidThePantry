# syntax=docker/dockerfile:1

# One image serving both halves: the API hosts the built SPA out of wwwroot, so
# the browser sees a single origin and cross-origin rules never come into play.
#
# A side effect worth knowing: the frontend builds at /src/web in here, a path
# with no "#" in it, so `npm run build` works normally. On the host it only works
# because the doodle SVG is referenced from public/ — see the README.

# ---- the SPA ---------------------------------------------------------------
FROM node:22-alpine AS web
WORKDIR /src/web

# Manifests first so a source-only change does not re-resolve every dependency.
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

# ---- the API ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src

# Both projects. The Worker is how the database gets bulk content, so an image
# without it leaves no way to run `scrape` or `promote` on the server short of
# installing the SDK there — which rather defeats shipping a container.
COPY RecipeApi/RecipeApi.csproj RecipeApi/
COPY RecipeApi.Worker/RecipeApi.Worker.csproj RecipeApi.Worker/
RUN dotnet restore RecipeApi/RecipeApi.csproj \
 && dotnet restore RecipeApi.Worker/RecipeApi.Worker.csproj

COPY RecipeApi/ RecipeApi/
COPY RecipeApi.Worker/ RecipeApi.Worker/

# The Worker goes in its own subdirectory rather than alongside the API. Both
# projects ship an appsettings.json, and publishing them to one directory fails
# outright with NETSDK1152 over the collision.
#
# A subdirectory is not merely a way around that: the Worker pins its
# ContentRootPath to AppContext.BaseDirectory, so it reads the appsettings.json
# sitting next to its own binary and the two configurations stay separate.
RUN dotnet publish RecipeApi/RecipeApi.csproj -c Release -o /publish --no-restore \
 && dotnet publish RecipeApi.Worker/RecipeApi.Worker.csproj -c Release -o /publish/worker --no-restore

# ---- runtime ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql probes for Kerberos/GSSAPI while negotiating authentication. Without
# this library it still connects perfectly well — it just prints
#   Cannot load library libgssapi_krb5.so.2
# on startup, which reads like a fatal failure and is the sort of standing noise
# that hides a real error when one eventually appears.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

COPY --from=api /publish ./

# Landing the SPA in wwwroot is what switches on static hosting and the SPA
# fallback in Program.cs — it checks for this directory rather than a flag.
COPY --from=web /src/web/dist ./wwwroot

# Non-root, using the account the .NET base images already provide.
USER $APP_UID

EXPOSE 8080

# No --migrate here on purpose. Migrations are a deliberate deploy step, so a
# failed one cannot crash-loop the app and several instances starting together
# cannot race each other:
#   docker run --rm <image> --migrate
#
# The Worker rides along in the same image under its own entry point, which the
# compose file exposes as a "worker" service:
#   docker compose -f docker-compose.prod.yml run --rm worker scrape /crawl/urls.txt
#   docker compose -f docker-compose.prod.yml run --rm worker promote
# Directly, that entry point is:  dotnet worker/RecipeApi.Worker.dll <command>
ENTRYPOINT ["dotnet", "RecipeApi.dll"]
