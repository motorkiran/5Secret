# Local Happy Path and Packaging Baseline

## Purpose

This document captures the first repeatable end-to-end V1 scenario and the minimum self-hosted packaging baseline that was proven locally.

The scenario below covers:

- Installation bootstrap.
- Topology and catalog setup.
- Appsettings import at installation scope.
- Effective preview.
- Publish.
- Agent enrollment and sync.
- Local runtime consumption through the sample provider.
- Draft update.
- Rollback.

## Prerequisites

- .NET 10 SDK.
- Node.js and npm.
- Docker Desktop or another local Docker engine.
- PowerShell.

## Local Dependency Stack

Start the local infrastructure first:

```powershell
docker compose up -d
```

Default local endpoints:

- PostgreSQL: `localhost:5432`
- Redis: `localhost:6380`

## Control Plane Startup

Persist the ASP.NET Data Protection key ring. Draft secrets will not survive a restart safely if the control plane key ring is ephemeral.

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
New-Item -ItemType Directory -Force -Path ".local\control-plane\keys" | Out-Null

$env:ASPNETCORE_URLS = 'http://localhost:5205'
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=secretmanager_dev;Username=secretmanager;Password=secretmanager'
$env:Infrastructure__Redis__Configuration = 'localhost:6380,password=secretmanager,abortConnect=false'
$env:Infrastructure__Redis__InstanceName = 'secretmanager:'
$env:Telemetry__EnableConsoleExporter = 'false'
$env:Infrastructure__DataProtection__KeyRingPath = (Resolve-Path '.local\control-plane\keys')

dotnet run --project ".\src\SecretManager.ControlPlane.Api\SecretManager.ControlPlane.Api.csproj" --urls "http://localhost:5205"
```

If a stale build output is locked by another process, run the built DLL directly instead of `dotnet run`.

## Optional Angular UI Startup

The UI was validated separately for bootstrap, topology, catalog, workflow, and runtime views.

```powershell
Set-Location "O:\DEV\Projects\SecretManager\src\SecretManager.Web"
npm install
npx ng serve --port 4201
```

The development proxy targets `http://localhost:5204` by default. For local UI smoke against a different control-plane port, update the proxy target or run the API on the expected port.

## Bootstrap and Operator Session

Bootstrap a clean installation and open a cookie-backed operator session:

```powershell
$bootstrapBody = @{
  installationName = 'Secret Manager Local'
  ownerUsername = 'rootadmin'
  ownerDisplayName = 'Root Admin'
  password = 'Passw0rd!Passw0rd!'
} | ConvertTo-Json

Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/auth/bootstrap' -Method Post -ContentType 'application/json' -Body $bootstrapBody

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{ username = 'rootadmin'; password = 'Passw0rd!Passw0rd!' } | ConvertTo-Json
Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/auth/login' -Method Post -WebSession $session -ContentType 'application/json' -Body $loginBody
```

## Minimal Topology and Catalog Setup

Create the environment, node group, managed node, application, and assignment required for preview and runtime delivery:

```powershell
$environment = Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/environments' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  name = 'Production'
  slug = 'production'
  description = 'Smoke production environment'
  isProtected = $true
} | ConvertTo-Json)

$nodeGroup = Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/node-groups' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  environmentId = $environment.environmentId
  name = 'Backend'
  slug = 'backend'
  description = 'Primary rollout group'
} | ConvertTo-Json)

$node = Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/nodes' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  environmentId = $environment.environmentId
  nodeGroupId = $nodeGroup.nodeGroupId
  name = 'node-01'
  hostname = 'node-01.local'
  platform = 'windows'
  status = 'Healthy'
  lastSeenAtUtc = $null
  agentVersion = 'pending'
  rolloutPolicyDefault = 'immediate'
} | ConvertTo-Json)

$application = Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/applications' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  name = 'Trading API'
  slug = 'trading-api'
  description = 'End-to-end smoke application'
  defaultIntegrationMode = 'runtime-api'
} | ConvertTo-Json)

Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/application-assignments' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  applicationId = $application.applicationId
  environmentId = $environment.environmentId
  nodeGroupId = $nodeGroup.nodeGroupId
  managedNodeId = $null
  enabled = $true
} | ConvertTo-Json)
```

## Import Baseline Drafts

Apply an appsettings-style baseline at installation scope. This is the slice that was explicitly revalidated after the installation-scope publish and preview fixes.

```powershell
Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/imports/appsettings/apply' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  applicationId = $application.applicationId
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
  changeNote = 'Imported smoke baseline'
} | ConvertTo-Json -Depth 5)
```

## Preview and Publish Baseline

Resolve the effective preview for the target node, then publish the first immutable version:

```powershell
$preview = Invoke-RestMethod -Uri "http://localhost:5205/api/v1/effective-snapshots/preview?applicationId=$($application.applicationId)&environmentId=$($environment.environmentId)&managedNodeId=$($node.nodeId)" -WebSession $session
$preview.itemCount

$baselinePublish = Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/publishes' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  applicationId = $application.applicationId
  environmentId = $environment.environmentId
  changeSummary = 'Publish imported baseline'
  rolloutPolicy = 'immediate'
} | ConvertTo-Json)
```

## Enroll and Start the Worker

Issue an enrollment token for the managed node:

```powershell
$token = Invoke-RestMethod -Uri "http://localhost:5205/api/v1/nodes/$($node.nodeId)/enrollment-token" -Method Post -WebSession $session
```

Start the worker with isolated local state and a persisted agent key ring:

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
New-Item -ItemType Directory -Force -Path ".local\agent-e2e\state" | Out-Null
New-Item -ItemType Directory -Force -Path ".local\agent-e2e\keys" | Out-Null

$env:ASPNETCORE_URLS = 'http://localhost:5159'
$env:Telemetry__EnableConsoleExporter = 'false'
$env:Infrastructure__Redis__Configuration = 'localhost:6380,password=secretmanager,abortConnect=false'
$env:Infrastructure__Redis__InstanceName = 'secretmanager:'
$env:Agent__ControlPlaneBaseUrl = 'http://localhost:5205'
$env:Agent__ManagedNodeId = $node.nodeId
$env:Agent__Hostname = 'node-01.local'
$env:Agent__Platform = 'windows'
$env:Agent__AgentVersion = 'e2e-smoke'
$env:Agent__EnrollmentToken = $token.enrollmentToken
$env:Agent__EnableBackgroundSync = 'true'
$env:Agent__SyncPollIntervalSeconds = '2'
$env:Agent__EnableChangeNotifications = 'false'
$env:Agent__LocalSnapshotStore__FilePath = (Resolve-Path '.local\agent-e2e\state').Path + '\snapshots.enc.json'
$env:Agent__RegistrationState__FilePath = (Resolve-Path '.local\agent-e2e\state').Path + '\registration.protected.json'
$env:Agent__DataProtection__KeyRingPath = (Resolve-Path '.local\agent-e2e\keys')

dotnet run --project ".\src\SecretManager.Agent.Worker\SecretManager.Agent.Worker.csproj" --urls "http://localhost:5159"
```

## Validate Local Runtime Consumption

Read the values through the sample configuration provider:

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
dotnet run --project ".\samples\SecretManager.ConfigurationProvider.Sample\SecretManager.ConfigurationProvider.Sample.csproj" -- --application-slug=trading-api --base-address=http://localhost:5159 --config-key=Trading:Core:MaxRetries
dotnet run --project ".\samples\SecretManager.ConfigurationProvider.Sample\SecretManager.ConfigurationProvider.Sample.csproj" -- --application-slug=trading-api --base-address=http://localhost:5159 --config-key=Trading:Core:ApiKey
```

Expected baseline values:

- `Trading:Core:MaxRetries = 3`
- `Trading:Core:ApiKey = bootstrap-secret`

## Update Drafts and Publish Again

Patch the installation-scope drafts, then publish a new version:

```powershell
$drafts = Invoke-RestMethod -Uri "http://localhost:5205/api/v1/draft-values?applicationId=$($application.applicationId)" -WebSession $session
$configItems = Invoke-RestMethod -Uri "http://localhost:5205/api/v1/config-items?applicationId=$($application.applicationId)" -WebSession $session

$apiKeyConfigItem = $configItems | Where-Object fullPath -eq 'Trading:Core:ApiKey'
$retriesConfigItem = $configItems | Where-Object fullPath -eq 'Trading:Core:MaxRetries'

$apiKeyDraft = $drafts | Where-Object configItemId -eq $apiKeyConfigItem.configItemId
$retriesDraft = $drafts | Where-Object configItemId -eq $retriesConfigItem.configItemId

Invoke-RestMethod -Uri "http://localhost:5205/api/v1/draft-values/$($apiKeyDraft.draftValueId)" -Method Patch -WebSession $session -ContentType 'application/json' -Body (@{
  valueJson = '"rotated-secret"'
  changeNote = 'Rotate API key for e2e validation'
} | ConvertTo-Json)

Invoke-RestMethod -Uri "http://localhost:5205/api/v1/draft-values/$($retriesDraft.draftValueId)" -Method Patch -WebSession $session -ContentType 'application/json' -Body (@{
  valueJson = '5'
  changeNote = 'Raise retry limit for e2e validation'
} | ConvertTo-Json)

$updatedPublish = Invoke-RestMethod -Uri 'http://localhost:5205/api/v1/publishes' -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  applicationId = $application.applicationId
  environmentId = $environment.environmentId
  changeSummary = 'Rotate API key and raise retry limit'
  rolloutPolicy = 'immediate'
} | ConvertTo-Json)
```

Re-run the sample and confirm:

- `Trading:Core:MaxRetries = 5`
- `Trading:Core:ApiKey = rotated-secret`

## Roll Back to the Baseline Version

```powershell
$versions = Invoke-RestMethod -Uri "http://localhost:5205/api/v1/published-versions?applicationId=$($application.applicationId)&environmentId=$($environment.environmentId)" -WebSession $session
$baselineVersion = $versions | Where-Object versionNumber -eq 1

Invoke-RestMethod -Uri "http://localhost:5205/api/v1/published-versions/$($baselineVersion.publishedVersionId)/rollback" -Method Post -WebSession $session -ContentType 'application/json' -Body (@{
  changeSummary = 'Rollback to bootstrap baseline after e2e validation'
} | ConvertTo-Json)
```

Re-run the sample and confirm the baseline values are restored:

- `Trading:Core:MaxRetries = 3`
- `Trading:Core:ApiKey = bootstrap-secret`

## First Packaging Baseline

The first self-hosted packaging baseline is intentionally simple:

1. One PostgreSQL instance with persistent storage.
2. One Redis instance for cache and invalidation acceleration.
3. One control-plane ASP.NET Core process with a persisted Data Protection key ring.
4. One Angular web build served behind a reverse proxy or static file host.
5. One agent worker process per managed node with persisted registration state, encrypted snapshot state, and a persisted agent key ring.

Minimum persistence surfaces that must survive restart:

- PostgreSQL data directory.
- Control-plane Data Protection key ring.
- Agent registration state file.
- Agent encrypted snapshot store file.
- Agent Data Protection key ring.

Minimum network assumptions:

- Operators can reach the Angular UI and the control-plane API.
- Agents can make outbound calls to the control-plane API.
- Applications can reach only the node-local worker runtime API.

This baseline does not yet include:

- High availability.
- External secret stores.
- SSO.
- Approval workflows.
- Production deployment manifests beyond the service inventory and persistence requirements above.