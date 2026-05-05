# 5Secret

5Secret is the working repository for SecretManager, a self-hosted configuration and secret control plane for distributed .NET microservice environments.

## Current Baseline

- Backend and agent stack: ASP.NET Core and .NET 10.
- Web UI stack: Angular.
- Architecture entry point: [docs/README.md](docs/README.md).
- Local installation and operator guide: [docs/architecture/10-local-installation-and-operator-guide.md](docs/architecture/10-local-installation-and-operator-guide.md).
- Server deployment and configuration guide: [docs/architecture/11-server-deployment-and-configuration.md](docs/architecture/11-server-deployment-and-configuration.md).
- Local happy-path and packaging guide: [docs/architecture/09-local-happy-path-and-packaging-baseline.md](docs/architecture/09-local-happy-path-and-packaging-baseline.md).

## Repository Layout

- `docs/` contains the product, architecture, security, and backlog documentation.
- `src/SecretManager.ControlPlane.Api` contains the control plane API shell.
- `src/SecretManager.ControlPlane.Application` contains the application layer shell.
- `src/SecretManager.Domain` contains the domain layer shell.
- `src/SecretManager.Infrastructure` contains the infrastructure layer shell.
- `src/SecretManager.Agent.Worker` contains the node-local agent shell.
- `src/SecretManager.Shared.Contracts` contains shared contracts.
- `src/SecretManager.Web` contains the Angular UI shell.

## Current Status

The documented MVP backlog is complete through `SM-028`, including the Angular operator UI, draft and publish workflow, agent status and audit views, and a validated local end-to-end happy path.

## Local Development Services

1. Copy `.env.example` to `.env` if you want to override the default local credentials.
2. Start dependencies with `docker compose up -d`.
3. Stop dependencies with `docker compose down`.

For the full local setup, startup sequence, operator workflow, and worker validation flow, use [docs/architecture/10-local-installation-and-operator-guide.md](docs/architecture/10-local-installation-and-operator-guide.md).
For server-side installation and deployment layout, use [docs/architecture/11-server-deployment-and-configuration.md](docs/architecture/11-server-deployment-and-configuration.md).

Default local development endpoints:

- PostgreSQL: `localhost:5432`
- Redis: `localhost:6380`

The API and agent development settings are already aligned to these defaults.