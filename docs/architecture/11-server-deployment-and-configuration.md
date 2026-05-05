# Server Deployment and Configuration Guide

## Purpose

This document covers the first practical server-side installation model for SecretManager.

It answers four deployment questions:

1. Which projects are actually deployed.
2. Which project runs on the central server and which one runs on managed nodes.
3. How connection strings and runtime settings are passed to each process.
4. How the central Angular operator UI is built and deployed.

This guide is intentionally aligned to the current V1 implementation. It describes the simplest supported self-hosted topology, not a future HA topology.

## Deployable Projects

Only three projects are deployed as runtime processes or runtime assets.

### Central Server

- `src/SecretManager.ControlPlane.Api`
  - Deploy this as the central ASP.NET Core control-plane process.
  - This process owns authentication, topology and catalog management, draft and publish APIs, rollback, audit, and agent coordination.

- `src/SecretManager.Web`
  - Build this as a static Angular SPA.
  - Deploy the generated static files behind a reverse proxy or static web server.
  - This is the central management UI.

### Managed Nodes

- `src/SecretManager.Agent.Worker`
  - Deploy one worker instance per managed machine.
  - This process enrolls with the control plane, syncs published snapshots, persists encrypted local state, and exposes the node-local runtime API.

## Projects That Are Not Deployed Separately

- `src/SecretManager.ControlPlane.Application`
- `src/SecretManager.Domain`
- `src/SecretManager.Infrastructure`
- `src/SecretManager.Shared.Contracts`

These are library projects consumed by the deployable applications.

- `src/SecretManager.ConfigurationProvider`
  - This is an application integration library, not a standalone server.
  - Your .NET applications reference this package or project to read from the node-local worker.

- `samples/SecretManager.ConfigurationProvider.Sample`
  - This is only for smoke testing and examples.
  - Do not deploy it as part of the production installation.

## Recommended V1 Topology

The current baseline is:

1. One PostgreSQL instance.
2. One Redis instance.
3. One central control-plane API process.
4. One reverse proxy or static host serving the Angular UI.
5. One worker process on each managed node.

Recommended network shape:

- Operators access the UI on a single public origin such as `https://secretmanager.example.com`.
- The reverse proxy serves the Angular files on `/`.
- The same reverse proxy forwards `/api/*` to the control-plane API.
- Each managed node runs its own worker, typically bound to a private interface or loopback.
- Applications on the same machine call the worker runtime API, not PostgreSQL or Redis directly.

## Same-Origin Requirement for the UI

The operator session uses a cookie with `SameSite=Strict`.

Because of that, the easiest and safest deployment model is:

- UI and API under the same origin.
- UI served from `/`.
- API reverse proxied from `/api/`.

Do not treat the current V1 UI as a cross-origin SPA plus remote API deployment unless you are also prepared to change authentication behavior.

## Build and Publish Outputs

### Control Plane API Publish

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
dotnet publish .\src\SecretManager.ControlPlane.Api\SecretManager.ControlPlane.Api.csproj -c Release -o .\.artifacts\control-plane-api
```

### Agent Worker Publish

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
dotnet publish .\src\SecretManager.Agent.Worker\SecretManager.Agent.Worker.csproj -c Release -o .\.artifacts\agent-worker
```

### Angular UI Build

```powershell
Set-Location "O:\DEV\Projects\SecretManager\src\SecretManager.Web"
npm ci
npm run build
```

Current build output location:

- `src/SecretManager.Web/dist/secretmanager-web`

Node.js is not required at runtime for the operator UI. Only the generated static files are deployed.

## Configuration Injection Model

The deployable .NET processes use normal ASP.NET Core configuration layering.

That means you can provide settings through:

- `appsettings.json`
- `appsettings.Production.json`
- environment variables
- host-level secret injection

For server deployments, prefer environment variables or a host secret store over committing production secrets into JSON files.

Environment variable naming follows the normal ASP.NET Core rule:

- `ConnectionStrings:Postgres` becomes `ConnectionStrings__Postgres`
- `Infrastructure:Redis:Configuration` becomes `Infrastructure__Redis__Configuration`

## Control Plane API Configuration

The control plane process requires these settings.

| Key | Required | Example | Notes |
| --- | --- | --- | --- |
| `ASPNETCORE_URLS` | Yes | `http://127.0.0.1:5204` | Internal Kestrel bind address behind the reverse proxy. |
| `ConnectionStrings__Postgres` | Yes | `Host=db.internal;Port=5432;Database=secretmanager_prod;Username=secretmanager;Password=strong-password` | Required by EF Core persistence. |
| `Infrastructure__Redis__Configuration` | Yes | `redis.internal:6379,password=strong-password,abortConnect=false` | Required for cache-backed snapshot delivery. |
| `Infrastructure__Redis__InstanceName` | Recommended | `secretmanager:` | Prefix for Redis keys. |
| `Infrastructure__DataProtection__KeyRingPath` | Yes | `/var/lib/secretmanager/control-plane/keys` | Must be persisted across restart. |
| `Telemetry__EnableConsoleExporter` | No | `false` | Optional console telemetry switch. |
| `Telemetry__OtlpEndpoint` | No | `http://otel-collector:4317` | Optional OTLP export destination. |

The API applies EF Core migrations automatically on startup. PostgreSQL must be reachable and the configured credential must have the required schema rights.

### Control Plane Example JSON

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=db.internal;Port=5432;Database=secretmanager_prod;Username=secretmanager;Password=strong-password"
  },
  "Infrastructure": {
    "Redis": {
      "Configuration": "redis.internal:6379,password=strong-password,abortConnect=false",
      "InstanceName": "secretmanager:"
    },
    "DataProtection": {
      "KeyRingPath": "/var/lib/secretmanager/control-plane/keys"
    }
  },
  "Telemetry": {
    "EnableConsoleExporter": false,
    "OtlpEndpoint": ""
  }
}
```

## Agent Worker Configuration

The worker process requires these settings.

| Key | Required | Example | Notes |
| --- | --- | --- | --- |
| `ASPNETCORE_URLS` | Yes | `http://127.0.0.1:5159` | Local runtime API bind address. |
| `Agent__ControlPlaneBaseUrl` | Yes | `https://secretmanager.example.com` | Base URL used for enroll, sync, and heartbeat. |
| `Agent__ManagedNodeId` | Yes | `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa` | Must match the managed node created in the control plane. |
| `Agent__Hostname` | Yes | `node-01.internal` | Reported to the control plane. |
| `Agent__Platform` | Yes | `linux` | Usually `linux` or `windows`. |
| `Agent__AgentVersion` | Yes | `1.0.0` | Operator-visible worker version string. |
| `Agent__EnrollmentToken` | First start only | `single-use-token` | Single-use token for initial enrollment. If local registration state is deleted later, issue a new token. |
| `Agent__EnableBackgroundSync` | Recommended | `true` | Enables polling and notification-driven sync. |
| `Agent__SyncPollIntervalSeconds` | Recommended | `30` | Background polling cadence. |
| `Agent__EnableChangeNotifications` | Recommended | `true` | Enables invalidation stream handling. |
| `Agent__NotificationReconnectDelaySeconds` | No | `5` | Delay between invalidation stream reconnect attempts. |
| `Agent__LocalSnapshotStore__FilePath` | Yes | `/var/lib/secretmanager/agent/snapshots.enc.json` | Persisted encrypted snapshot store. |
| `Agent__RegistrationState__FilePath` | Yes | `/var/lib/secretmanager/agent/registration.protected.json` | Persisted agent registration state. |
| `Agent__DataProtection__KeyRingPath` | Yes | `/var/lib/secretmanager/agent/keys` | Must survive restart. |
| `Agent__Projection__Targets__0__ApplicationSlug` | Optional | `trading-api` | Use only if projecting snapshots to a file for local apps. |
| `Agent__Projection__Targets__0__FilePath` | Optional | `/etc/secretmanager/trading-api.settings.json` | Output path for file projection mode. |
| `Infrastructure__Redis__Configuration` | Recommended | `redis.internal:6379,password=strong-password,abortConnect=false` | Reuse the same Redis if available. |
| `Infrastructure__Redis__InstanceName` | Recommended | `secretmanager:` | Should match the central installation. |
| `Telemetry__EnableConsoleExporter` | No | `false` | Optional console telemetry switch. |
| `Telemetry__OtlpEndpoint` | No | `http://otel-collector:4317` | Optional OTLP export destination. |

### Worker Example JSON

```json
{
  "Agent": {
    "ControlPlaneBaseUrl": "https://secretmanager.example.com",
    "ManagedNodeId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "Hostname": "node-01.internal",
    "Platform": "linux",
    "AgentVersion": "1.0.0",
    "EnrollmentToken": "single-use-token",
    "EnableBackgroundSync": true,
    "SyncPollIntervalSeconds": 30,
    "EnableChangeNotifications": true,
    "NotificationReconnectDelaySeconds": 5,
    "LocalSnapshotStore": {
      "FilePath": "/var/lib/secretmanager/agent/snapshots.enc.json"
    },
    "RegistrationState": {
      "FilePath": "/var/lib/secretmanager/agent/registration.protected.json"
    },
    "DataProtection": {
      "KeyRingPath": "/var/lib/secretmanager/agent/keys"
    },
    "Projection": {
      "Targets": [
        {
          "ApplicationSlug": "trading-api",
          "FilePath": "/etc/secretmanager/trading-api.settings.json"
        }
      ]
    }
  },
  "Infrastructure": {
    "Redis": {
      "Configuration": "redis.internal:6379,password=strong-password,abortConnect=false",
      "InstanceName": "secretmanager:"
    }
  }
}
```

## What Must Persist on Disk

These paths are not optional runtime scratch space.

### Central Side

- PostgreSQL data directory.
- Redis data directory if Redis persistence is enabled.
- Control-plane Data Protection key ring.

### Managed Node Side

- Agent registration state file.
- Agent encrypted snapshot store.
- Agent Data Protection key ring.
- Optional projected file targets if local applications read them directly.

## Central Management UI Deployment

The Angular operator UI is deployed as static files.

Recommended approach:

1. Build `src/SecretManager.Web` with `npm run build`.
2. Copy the contents of `dist/secretmanager-web` to a static web root such as `/var/www/secretmanager-web`.
3. Use a reverse proxy to serve the SPA on `/`.
4. Proxy `/api/` to the control-plane API Kestrel address.

### Example Nginx Configuration

```nginx
server {
    listen 443 ssl;
    server_name secretmanager.example.com;

    root /var/www/secretmanager-web;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://127.0.0.1:5204/api/;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

This keeps the UI and API on the same origin, which matches the current cookie authentication model.

## Control Plane Linux Service Example

```ini
[Unit]
Description=SecretManager Control Plane API
After=network.target

[Service]
WorkingDirectory=/opt/secretmanager/control-plane
ExecStart=/usr/bin/dotnet /opt/secretmanager/control-plane/SecretManager.ControlPlane.Api.dll
Restart=always
RestartSec=5
User=secretmanager
Environment=ASPNETCORE_URLS=http://127.0.0.1:5204
Environment=ConnectionStrings__Postgres=Host=db.internal;Port=5432;Database=secretmanager_prod;Username=secretmanager;Password=strong-password
Environment=Infrastructure__Redis__Configuration=redis.internal:6379,password=strong-password,abortConnect=false
Environment=Infrastructure__Redis__InstanceName=secretmanager:
Environment=Infrastructure__DataProtection__KeyRingPath=/var/lib/secretmanager/control-plane/keys
Environment=Telemetry__EnableConsoleExporter=false

[Install]
WantedBy=multi-user.target
```

## Worker Linux Service Example

```ini
[Unit]
Description=SecretManager Agent Worker
After=network.target

[Service]
WorkingDirectory=/opt/secretmanager/agent
ExecStart=/usr/bin/dotnet /opt/secretmanager/agent/SecretManager.Agent.Worker.dll
Restart=always
RestartSec=5
User=secretmanager-agent
Environment=ASPNETCORE_URLS=http://127.0.0.1:5159
Environment=Agent__ControlPlaneBaseUrl=https://secretmanager.example.com
Environment=Agent__ManagedNodeId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
Environment=Agent__Hostname=node-01.internal
Environment=Agent__Platform=linux
Environment=Agent__AgentVersion=1.0.0
Environment=Agent__EnrollmentToken=single-use-token
Environment=Agent__EnableBackgroundSync=true
Environment=Agent__SyncPollIntervalSeconds=30
Environment=Agent__EnableChangeNotifications=true
Environment=Agent__NotificationReconnectDelaySeconds=5
Environment=Agent__LocalSnapshotStore__FilePath=/var/lib/secretmanager/agent/snapshots.enc.json
Environment=Agent__RegistrationState__FilePath=/var/lib/secretmanager/agent/registration.protected.json
Environment=Agent__DataProtection__KeyRingPath=/var/lib/secretmanager/agent/keys
Environment=Infrastructure__Redis__Configuration=redis.internal:6379,password=strong-password,abortConnect=false
Environment=Infrastructure__Redis__InstanceName=secretmanager:
Environment=Telemetry__EnableConsoleExporter=false

[Install]
WantedBy=multi-user.target
```

## Deployment Order

Use this order for a clean first installation:

1. Provision PostgreSQL and Redis.
2. Publish and deploy `SecretManager.ControlPlane.Api`.
3. Set the control-plane environment variables and persisted key-ring path.
4. Start the control-plane service and verify it can reach PostgreSQL and Redis.
5. Build and deploy `SecretManager.Web` static files.
6. Put the UI behind the reverse proxy on the same origin as `/api`.
7. Open the UI and complete the first bootstrap.
8. Create environments, node groups, managed nodes, applications, and assignments.
9. Publish `SecretManager.Agent.Worker` to each managed node.
10. Issue an enrollment token from the control plane for each node.
11. Start the worker with its persisted state paths.
12. Validate runtime delivery from a local application or the sample provider.

## Operational Notes

- If the control-plane Data Protection key ring changes unexpectedly, encrypted draft secrets will not decrypt correctly after restart.
- If the worker Data Protection key ring or registration file is lost, the node must be re-enrolled with a new token.
- If the worker snapshot store is lost, the worker can recover by syncing again, but offline recovery is lost until a new snapshot is persisted.
- The current V1 baseline is process-based deployment with a reverse proxy. Container orchestration and HA are future work, not the documented baseline.