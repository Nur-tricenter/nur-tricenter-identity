# nur-tricenter-identity

Identity microservice for the Nur-Tricenter platform: turns a credential into a JWT, publishes account
events through a transactional outbox, and seeds the default administrator. DDD + Clean Architecture,
.NET 8, EF Core + PostgreSQL, RabbitMQ, Consul service discovery. **This service is the identity kernel
that every other microservice on the platform depends on** — it is the only place that mints tokens and
the only publisher of `UserPublished` / `UserChanged`.

## Purpose

- Authenticate a user name + password and issue a bearer JWT bound to the user's scopes.
- Expose the authenticated principal (`/me`) read back from the token.
- Administer accounts (create) — requires `identity.user:manage`.
- Publish account lifecycle events so other services can project users and permissions.
- Seed the default administrator on a fresh stack so it is usable without a manual first login.

## Stack

| Component | Technology |
|---|---|
| Runtime | .NET 8 (`net8.0`) |
| Architecture | DDD + Clean Architecture (no separate Domain project; invariants live in Application) |
| CQRS | MediatR |
| ORM | Entity Framework Core 9.0.4 |
| Database | PostgreSQL (one DB: `nur_tricenter_identity`) |
| Messaging | RabbitMQ via `Joseco.Communication.External.RabbitMQ` |
| Outbox | `Joseco.Outbox.EFCore` (schema `outbox`) |
| Discovery | Consul |
| Security | `Nur.Tricenter.Security` (JWT, one policy per permission) |
| Access | `Nur.Tricenter.Access` (permission names, platform expansion) |
| Integration | `Nur.Tricenter.Integration` (shared message contracts) |
| Telemetry | `Nur.Tricenter.Observability` |
| API docs | Swagger / OpenAPI (Development) |

## Project structure

```
nur-tricenter-identity/
├── src/
│   ├── Identity.App/              # HTTP host: controllers, Program, health, Consul registration
│   ├── Identity.Application/      # User aggregate, domain events, handlers, abstractions, contracts
│   ├── Identity.Infrastructure/   # DbContext, repositories, outbox, RabbitMQ publisher, seeder, security wiring
│   └── Identity.WorkerService/    # Outbox drain -> RabbitMQ, admin seed
├── identity.compose.yml           # Platform compose (2 processes + migrate job)
├── Directory.Packages.props       # Central package versions
├── Directory.Build.props
├── NuGet.config                   # GitHub Packages feed for the kernels
└── Identity.slnx
```

### Dependency rule

```
Application     →  Domain invariants (no EF, no broker)
Infrastructure  →  Application
Identity.App    →  Infrastructure
WorkerService   →  Infrastructure
```

## API endpoints (prefix `/api/v1`, platform convention)

### Auth (`/api/v1/auth`)

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/v1/auth/login` | anonymous | Exchange user name + password for a bearer JWT |
| GET  | `/api/v1/auth/me` | bearer | Read the authenticated principal from the token |

### Users (`/api/v1/users`)

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/v1/users` | `identity.user:manage` | Create an account; publishes `UserCreated` via outbox; 409 if the name is taken |

The `/api/v1` prefix is not cosmetic: the gateway applies `PathPrefix: /api` when forwarding, so a service exposing `/api/...` would be requested as `/api/api/...` upstream and 404. Every service aligns here so one gateway route serves all.

## Authentication & tokens

- `JwtOptions` (key, issuer, audience, lifetime) come from `Jwt__*` env vars. `AddSecurity` refuses to compose without a signing key.
- The token carries the user's scopes as `scope` claims. `IdentityPermissions.Expand` turns a wildcard (`identity:*`) into concrete scopes (`identity.user:read`, `identity.user:manage`, …).
- The built-in administrator holds every scope through the expanded wildcard; a regular account holds only what it was granted.
- **The same key/issuer/audience must be used by every validator** (catalog, gateway if/when auth is enabled). A mismatch 401s every request.
- Platform-scoped tokens: the latest commit mints tokens scoped to the platform audience so downstream services can trust the `sub` claim.

## Outbox & events

```
Command → DomainDbContext.SaveChanges()
        → domain event → outbox.outboxMessage   (same transaction)
        → WorkerService: OutboxProcessor
        → PublishIntegrationEvent* → RabbitMQ (exchange user-published / user-updated)
```

- Published contracts: `UserPublished` (creation) and `UserChanged` (mutation), both in `Nur.Tricenter.Integration.Messages.Identity`. Publisher and consumer compile against the **same** type — duplicated contracts drift silently.
- The worker also seeds the default administrator (idempotent by user name; never resets an existing credential).

## Configuration

| Key | Source | Meaning |
|---|---|---|
| `ConnectionStrings:IdentityDatabaseConnectionString` | env var | **Required.** Postgres connection. |
| `Jwt:SecretKey` / `ValidIssuer` / `ValidAudience` / `Lifetime` | env var (`Jwt__*`) | **Required.** Signing + validation. |
| `DefaultUser:Enabled` / `UserName` / `Password` / `Email` | env var | Seeds the admin on startup. |
| `ServiceRegistration:ServiceDiscoveryAddress` | env var | Consul URL. Enables registration + DB health check when set. |
| `ServiceRegistration:Host` / `Port` / `ServiceName` | env var | Pinned (not sniffed) registration. `ServiceName = identity-service`. |
| `RabbitMqSettings:*` | env var | Broker host/credentials (worker only). |

No credentials in version-controlled files. The connection string arrives from an env var or a local, git-ignored `appsettings.Development.json`.

## Migrations

The schema is applied by an explicit one-shot step, never on boot:

```bash
dotnet run --project src/Identity.App/Identity.App.csproj -- --migrate
```

Applies the EF migrations, exits 0 on success / 1 on failure, idempotent. In compose this is the `identity-migrate` job that gates `identity-api` (`service_completed_successfully`).

## Running locally

### Option 1 — the orchestrator (recommended)

```bash
cd <folder containing nur-tricenter-orchestrator>
./scripts/start-stack.sh          # or start-stack.ps1 on Windows
```

Brings up Postgres + RabbitMQ + Consul + identity (api + worker + migrate) + catalog + gateway. Identity is discovered by the gateway as `identity-service`.

### Option 2 — standalone compose

```bash
docker compose up -d              # identity's own Postgres + network
```

### Option 3 — local run

```bash
dotnet run --project src/Identity.App/Identity.App.csproj
dotnet run --project src/Identity.WorkerService/Identity.WorkerService.csproj
```

## How it integrates with the rest of the platform

- **Gateway**: registers as `identity-service` in Consul; the gateway routes `/api/identity/**` to it. (Auth middleware on the gateway is deferred — identity is the issuer, validators are downstream.)
- **Catalog (and future services)**: consume `Nur.Tricenter.Integration` to deserialize `UserPublished`/`UserChanged` from the `user-published` / `user-updated` exchanges, then project users and scopes locally.
- **Shared permissions**: `IdentityPermissions` + `IdentityPermissions.Expand` (in `Nur.Tricenter.Access`) define the platform-wide permission set and the wildcard expansion. A new service that needs new permissions adds them to the kernel, not here.

## How to add a new microservice that consumes identity

1. Reference `Nur.Tricenter.Access`, `Nur.Tricenter.Integration`, `Nur.Tricenter.Security`, `Nur.Tricenter.Observability` from the GitHub Packages feed (see `NuGet.config`).
2. Consume the `user-published` / `user-updated` exchanges with the shared `UserPublished` / `UserChanged` contracts.
3. Validate JWTs with the **same** `Jwt:SecretKey / ValidIssuer / ValidAudience` as identity.
4. Grant scopes to new accounts by passing concrete scope strings to `POST /api/v1/users`; use `IdentityPermissions.Expand` semantics if you introduce wildcards.
5. Add the new database to the orchestrator's `postgres-init/01-create-databases.sql` and a compose file with its own profile (see orchestrator README).

## Resources

| Resource | URL |
|---|---|
| API (via gateway) | http://localhost:15005/api/identity/v1/... |
| Direct (dev) | http://localhost:8080/api/v1/... |
| Swagger (dev) | http://localhost:8080/swagger |
| Health | http://localhost:8080/health |
| Consul | http://localhost:8500 |
