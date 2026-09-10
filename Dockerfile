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

COPY RecipeApi/RecipeApi.csproj RecipeApi/
RUN dotnet restore RecipeApi/RecipeApi.csproj

COPY RecipeApi/ RecipeApi/
RUN dotnet publish RecipeApi/RecipeApi.csproj -c Release -o /publish --no-restore

# ---- runtime ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

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
ENTRYPOINT ["dotnet", "RecipeApi.dll"]
