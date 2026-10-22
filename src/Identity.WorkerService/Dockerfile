# syntax=docker/dockerfile:1

# Multi-stage: restore with the SDK, ship only the published output on the ASP.NET runtime.
#
# BUILD CONTEXT IS THIS REPOSITORY:
#
#   docker build -f src/Identity.WorkerService/Dockerfile -t nur-tricenter-identity-worker .
#
# Same two constraints as the API image, and for the same reasons:
#
#  1. SDK and runtime tags must match net8.0.
#  2. Private-feed credentials arrive as build secrets, and the variable is NUGET_TOKEN (the name
#     NuGet.config expands), not NUGET_AUTH_TOKEN.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# --- Restore layer ------------------------------------------------------------
COPY Directory.Packages.props Directory.Build.props NuGet.config ./
COPY src/Identity.Application/Identity.Application.csproj src/Identity.Application/
COPY src/Identity.Infrastructure/Identity.Infrastructure.csproj src/Identity.Infrastructure/
COPY src/Identity.WorkerService/Identity.WorkerService.csproj src/Identity.WorkerService/

RUN --mount=type=secret,id=nuget_username \
    --mount=type=secret,id=nuget_token \
    export NUGET_USERNAME="$(cat /run/secrets/nuget_username)" && \
    export NUGET_TOKEN="$(cat /run/secrets/nuget_token)" && \
    dotnet restore src/Identity.WorkerService/Identity.WorkerService.csproj

# --- Sources and publish ------------------------------------------------------
COPY . .

RUN dotnet publish src/Identity.WorkerService/Identity.WorkerService.csproj \
        -c Release \
        -o /app/publish \
        --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# No curl here: this host serves no HTTP, so its healthcheck reads the process table instead of
# probing a port that does not exist.

COPY --from=build /app/publish .

ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Identity.WorkerService.dll"]