# syntax=docker/dockerfile:1

# Multi-stage: restore with the SDK, ship only the published output on the ASP.NET runtime.
#
# BUILD CONTEXT IS THIS REPOSITORY:
#
#   docker build -f src/Identity.App/Dockerfile -t nur-tricenter-identity-api .
#
# The platform kernels are NuGet packages from GitHub Packages, not a sibling folder, so nothing
# outside this repository is needed and every project reference resolves through restore.
#
# Three things this file has to get right, and each one fails in a way that reads like a different
# problem than it is:
#
#  1. The SDK and runtime tags must match this service's TargetFramework (net8.0). An sdk:10.0 tag
#     building a net8.0 project restores fine and then dies at run time on a missing framework.
#
#  2. NuGet.config carries the organization feed, and restore needs credentials for a private feed.
#     They arrive as build secrets — never bake a token into an image layer.
#
#  3. The variable name is NUGET_TOKEN, not NUGET_AUTH_TOKEN. NuGet.config expands
#     %NUGET_USERNAME% and %NUGET_TOKEN% from the environment, and a token exported under a
#     different name leaves the feed request unauthenticated: NU1301 401 on EVERY Nur.Tricenter.*
#     package, which reads like a missing package rather than a missing environment variable.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# --- Restore layer ------------------------------------------------------------
# Only the manifests first, so editing source does not invalidate the NuGet cache.
# Directory.Packages.props comes first: central package management reads the versions from it,
# so NuGet needs it before any manifest is restored. Directory.Build.props sits next to it for
# the same reason — it supplies TargetFramework.
COPY Directory.Packages.props Directory.Build.props NuGet.config ./
COPY src/Identity.Application/Identity.Application.csproj src/Identity.Application/
COPY src/Identity.Infrastructure/Identity.Infrastructure.csproj src/Identity.Infrastructure/
COPY src/Identity.App/Identity.App.csproj src/Identity.App/

# Secrets are mounted by id only; the values come from the docker build command line. Declaring a
# source in BOTH places fails with "both source and id can't be set".
RUN --mount=type=secret,id=nuget_username \
    --mount=type=secret,id=nuget_token \
    export NUGET_USERNAME="$(cat /run/secrets/nuget_username)" && \
    export NUGET_TOKEN="$(cat /run/secrets/nuget_token)" && \
    dotnet restore src/Identity.App/Identity.App.csproj

# --- Sources and publish ------------------------------------------------------
COPY . .

RUN dotnet publish src/Identity.App/Identity.App.csproj \
        -c Release \
        -o /app/publish \
        --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl backs the container healthcheck. The aspnet image does not ship it, and a healthcheck whose
# command is missing fails with "executable file not found" rather than reporting unhealthy, which
# reads as a mystery until you inspect the container.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# The `--migrate` argument the compose one-shot step appends lands here and turns this same image
# into the schema deployment step: the host is built, migrations are applied, and the process exits
# 0 on success or 1 on failure.
ENTRYPOINT ["dotnet", "Identity.App.dll"]