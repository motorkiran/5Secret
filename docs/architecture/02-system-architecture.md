# System Architecture

## Architectural Style

SecretManager uses a control-plane-plus-data-plane architecture.

- The control plane owns management APIs, UI, authorization, persistence, versioning, audit, and publish orchestration.
- The data plane is the node-local runtime layer that serves applications and survives temporary control plane outages.

## V1 Topology

### Control Plane Components

1. Angular Web UI.
2. ASP.NET Core control plane API.
3. Domain services for drafts, publish, rollback, and authorization.
4. Snapshot resolver.
5. Audit writer.
6. PostgreSQL.
7. Redis.

### Data Plane Components

1. Node-local agent.
2. Agent in-memory cache.
3. Agent encrypted local snapshot store.
4. Local runtime API.
5. Optional file projection worker.
6. Optional .NET configuration provider or SDK.

## Deployment Assumptions

1. Everything runs in Docker containers unless a customer explicitly chooses another host model.
2. Control plane is installed once per customer environment.
3. One agent runs on each managed machine.
4. The agent only serves applications on the same machine.
5. The control plane is stateless except for PostgreSQL and Redis.

## Why PostgreSQL Plus Redis Plus Local Cache

The product originally started from a `memory -> redis -> postgres` idea. That idea is preserved in spirit, but refined into a safer model.

### Final Read and Write Model

- PostgreSQL is authoritative for writes and historical state.
- Redis accelerates distribution of published snapshots and change notifications.
- The agent keeps the active snapshot in memory.
- The agent keeps an encrypted local snapshot on disk for restart and offline use.
- The application should read from memory through the local agent or a projected file, not from Redis or PostgreSQL on every request.

This model protects correctness and auditability while still meeting the performance goal.

## Component Responsibilities

### Control Plane API

Responsible for:

- Authentication and session management.
- Authorization checks.
- CRUD for environments, nodes, applications, namespaces, and config items.
- Draft editing.
- Publish and rollback operations.
- Audit event capture.
- Agent enrollment and coordination.

### Snapshot Resolver

Responsible for computing the effective runtime snapshot by applying inheritance and overrides in deterministic order.

### PostgreSQL

Responsible for:

- Authoritative state.
- Immutable published versions.
- Draft data.
- Audit records.
- User and authorization metadata.
- Agent registrations and health history.

### Redis

Responsible for:

- Published snapshot acceleration.
- Change invalidation fan-out.
- Short-lived coordination data.

Redis must not be treated as the sole owner of active configuration.

### Node-Local Agent

Responsible for:

- Enrolling with the control plane.
- Maintaining a live or near-live link to change notifications.
- Pulling updated snapshots.
- Keeping hot in-memory state.
- Persisting encrypted offline snapshots.
- Serving local runtime requests.
- Projecting files when that integration mode is enabled.

## Runtime Delivery Modes

V1 supports two runtime integration modes.

### Mode A: Local Runtime API

Applications call the node-local agent through a loopback HTTP endpoint or a local IPC mechanism. A .NET provider or SDK can cache the response in-process and reload on signal.

### Mode B: File Projection

The agent writes an effective JSON file into a shared location. The application uses `.NET` configuration reload behavior to pick up changes when allowed by rollout policy.

Mode A is more dynamic. Mode B is easier for legacy applications.

## Rollout and Delivery Path

1. Operator publishes a new version.
2. Control plane writes immutable version data to PostgreSQL.
3. Control plane computes effective snapshots.
4. Control plane stores accelerated snapshot data in Redis.
5. Control plane signals relevant agents.
6. Agent pulls the new snapshot from Redis when available, otherwise from the control plane.
7. Agent validates and activates the snapshot according to rollout policy.
8. Agent updates memory and encrypted local storage.
9. Local application reload behavior follows the chosen rollout policy.

## High Availability Strategy

### V1 Decision

V1 uses a single control plane instance.

### Rationale

1. It keeps installation and support complexity under control.
2. It lets the first release focus on correctness and operator workflows.
3. It still allows resilience through proper PostgreSQL backup, Redis backup, and agent offline behavior.

### Future Direction

The control plane codebase should remain stateless so that active-passive or active-active modes can be added later without a major rewrite.

## Network and Trust Model

1. UI and browsers talk only to the control plane.
2. Agents establish outbound connectivity to the control plane.
3. Agents do not expose public cross-node APIs.
4. Applications talk only to the local agent or local projected files.
5. Sensitive traffic must use TLS.

## Suggested Implementation Stack

The current implementation baseline is:

- ASP.NET Core on .NET 10 for the control plane API and agent services.
- Angular for the operator-facing web UI.
- PostgreSQL for relational persistence.
- Redis for acceleration and coordination.
- Docker Compose for local development and first deployment packaging.
- OpenTelemetry for traces, metrics, and logs.

## Operational Expectations

1. The product must survive control plane restarts without losing published state.
2. The agent must survive node restarts without losing the last valid snapshot.
3. Publish operations must be atomic from the operator point of view.
4. Rollback must be a first-class operation, not a manual repair task.