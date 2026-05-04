# Local Installation and Operator Guide

## Purpose

This document is the shortest end-to-end guide for installing, starting, and using SecretManager locally.

Use this guide when you want to:

- Prepare a fresh developer machine.
- Start the control plane, Angular UI, and agent locally.
- Bootstrap the first operator account.
- Create the minimum topology and catalog records.
- Publish configuration and verify runtime delivery.

For the deeper validated smoke scenario and packaging baseline, continue with [Local Happy Path and Packaging Baseline](09-local-happy-path-and-packaging-baseline.md).

## What You Will Run

The local setup uses four moving parts:

1. PostgreSQL as the system of record.
2. Redis as cache and invalidation accelerator.
3. The ASP.NET Core control plane API.
4. The Angular operator UI.

The optional fifth process is the node-local worker used for runtime delivery validation.

## Prerequisites

- Windows with PowerShell.
- .NET 10 SDK.
- Node.js and npm.
- Docker Desktop or another Docker engine.

## Repository Bootstrap

Open PowerShell in the repository root:

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
```

Optional local overrides:

```powershell
Copy-Item .env.example .env
```

Restore and build the solution:

```powershell
dotnet restore .\SecretManager.slnx
dotnet build .\SecretManager.slnx
```

Install UI dependencies:

```powershell
Set-Location ".\src\SecretManager.Web"
npm install
Set-Location "..\.."
```

## Start Local Infrastructure

Start PostgreSQL and Redis:

```powershell
docker compose up -d
```

Default local endpoints:

- PostgreSQL: `localhost:5432`
- Redis: `localhost:6380`

Stop them later with:

```powershell
docker compose down
```

## Start the Control Plane API

Persist the control-plane Data Protection keys. Secret draft decryption must survive restart.

The recommended local API port is `5204` because the Angular development proxy already points there.

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
New-Item -ItemType Directory -Force -Path ".local\control-plane\keys" | Out-Null

$env:ASPNETCORE_URLS = 'http://localhost:5204'
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=secretmanager_dev;Username=secretmanager;Password=secretmanager'
$env:Infrastructure__Redis__Configuration = 'localhost:6380,password=secretmanager,abortConnect=false'
$env:Infrastructure__Redis__InstanceName = 'secretmanager:'
$env:Telemetry__EnableConsoleExporter = 'false'
$env:Infrastructure__DataProtection__KeyRingPath = (Resolve-Path '.local\control-plane\keys')

dotnet run --project ".\src\SecretManager.ControlPlane.Api\SecretManager.ControlPlane.Api.csproj"
```

If you choose a different API port, update [src/SecretManager.Web/proxy.conf.json](src/SecretManager.Web/proxy.conf.json) or start Angular against the same port mapping.

## Start the Angular Operator UI

Run the operator UI in a second terminal:

```powershell
Set-Location "O:\DEV\Projects\SecretManager\src\SecretManager.Web"
npm start -- --port 4201
```

Open the UI at:

- `http://localhost:4201`

## First-Time Bootstrap

On a fresh database, the first page is the bootstrap flow.

1. Open `http://localhost:4201`.
2. Create the installation name.
3. Create the first operator account.
4. Sign in and enter the authenticated operator shell.

After bootstrap completes, the bootstrap route closes and subsequent access uses the login page.

## Minimum Operator Workflow

Once you are signed in, use the UI in this order.

### 1. Topology

Create the deployment surface in the `Topology` page:

- One environment, for example `Production`.
- One node group, for example `Backend`.
- One managed node, for example `node-01.local`.

The managed node is the future worker target used by preview and runtime validation.

### 2. Catalog

Create the runtime model in the `Catalog` page:

- One application, for example `Trading API`.
- One namespace, for example `Trading:Core`.
- One or more config items such as `ApiKey` and `MaxRetries`.
- One application assignment connecting the application to the environment or node group.

### 3. Workflow

Use the `Workflow` page to manage runtime state:

- Create or edit draft values.
- Load the effective preview for a selected node.
- Publish an immutable version.
- Inspect diffs between versions.
- Roll back to an earlier version.

### 4. Runtime

Use the `Runtime` page to inspect delivery state:

- Agent freshness and health.
- Current reported published version.
- Recent audit events and payload detail.

## Optional Import Flow

If you want to seed configuration from an `appsettings`-style payload, use the import endpoints.

Example after login with a cookie-backed PowerShell session:

```powershell
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{ username = 'rootadmin'; password = 'Passw0rd!Passw0rd!' } | ConvertTo-Json
Invoke-RestMethod -Uri 'http://localhost:5204/api/v1/auth/login' -Method Post -WebSession $session -ContentType 'application/json' -Body $loginBody | Out-Null

Invoke-RestMethod -Uri 'http://localhost:5204/api/v1/imports/appsettings/apply' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  applicationId = '<application-id>'
  scopeType = 'Installation'
  scopeId = '00000000-0000-0000-0000-000000000001'
  jsonPayload = @'
{
  "Trading": {
    "Core": {
      "ApiKey": "bootstrap-secret",
      "MaxRetries": 3
    }
  }
}
'@
  secretFullPaths = @('Trading:Core:ApiKey')
  changeNote = 'Imported baseline configuration'
} | ConvertTo-Json -Depth 5)
```

After import, return to the `Workflow` page, load preview, and publish.

## Start a Local Worker

The worker is only needed when you want to verify runtime delivery on a managed node.

First issue an enrollment token for the managed node:

```powershell
$token = Invoke-RestMethod -Uri "http://localhost:5204/api/v1/nodes/<managed-node-id>/enrollment-token" -Method Post -WebSession $session
```

Then start the worker in a third terminal:

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
New-Item -ItemType Directory -Force -Path ".local\agent\state" | Out-Null
New-Item -ItemType Directory -Force -Path ".local\agent\keys" | Out-Null

$env:ASPNETCORE_URLS = 'http://localhost:5159'
$env:Telemetry__EnableConsoleExporter = 'false'
$env:Infrastructure__Redis__Configuration = 'localhost:6380,password=secretmanager,abortConnect=false'
$env:Infrastructure__Redis__InstanceName = 'secretmanager:'
$env:Agent__ControlPlaneBaseUrl = 'http://localhost:5204'
$env:Agent__ManagedNodeId = '<managed-node-id>'
$env:Agent__Hostname = 'node-01.local'
$env:Agent__Platform = 'windows'
$env:Agent__AgentVersion = 'local-dev'
$env:Agent__EnrollmentToken = $token.enrollmentToken
$env:Agent__EnableBackgroundSync = 'true'
$env:Agent__SyncPollIntervalSeconds = '2'
$env:Agent__EnableChangeNotifications = 'false'
$env:Agent__LocalSnapshotStore__FilePath = (Resolve-Path '.local\agent\state').Path + '\snapshots.enc.json'
$env:Agent__RegistrationState__FilePath = (Resolve-Path '.local\agent\state').Path + '\registration.protected.json'
$env:Agent__DataProtection__KeyRingPath = (Resolve-Path '.local\agent\keys')

dotnet run --project ".\src\SecretManager.Agent.Worker\SecretManager.Agent.Worker.csproj" --urls "http://localhost:5159"
```

Persist both the worker state files and the worker Data Protection key ring. Restart-safe registration and encrypted local snapshots depend on them.

## Validate Runtime Consumption

Use the sample provider app to read values from the node-local runtime API:

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
dotnet run --project ".\samples\SecretManager.ConfigurationProvider.Sample\SecretManager.ConfigurationProvider.Sample.csproj" -- --application-slug=trading-api --base-address=http://localhost:5159 --config-key=Trading:Core:MaxRetries
```

Repeat the same command for any other config key you want to verify.

## Recommended Local Session

For a normal developer session, this order is sufficient:

1. `docker compose up -d`
2. Start the control plane on `5204`
3. Start Angular on `4201`
4. Bootstrap or log in
5. Make topology, catalog, and workflow changes in the UI
6. Start a worker only when runtime validation is needed
7. Stop the API, UI, and worker with `Ctrl+C`
8. Stop infrastructure with `docker compose down`

## Troubleshooting

### UI loads but API calls fail

The Angular development proxy expects the API on `http://localhost:5204` by default. If the API runs elsewhere, update [src/SecretManager.Web/proxy.conf.json](src/SecretManager.Web/proxy.conf.json) or restart the API on `5204`.

### Draft secrets fail after restart

Do not use an ephemeral Data Protection configuration. Persist:

- `Infrastructure:DataProtection:KeyRingPath` for the control plane.
- `Agent:DataProtection:KeyRingPath` for the worker.

### Worker starts but cannot sync

Check these first:

- Redis is running.
- `Agent__ControlPlaneBaseUrl` points to the active API port.
- The enrollment token was issued for the same managed node ID.
- The node has an application assignment and at least one published version.

### Angular development server behaves inconsistently

Stop the existing watcher and start a fresh `npm start -- --port 4201` session. A stale watcher can keep outdated module state alive.