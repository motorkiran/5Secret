# Agent Sync and Runtime Behavior

## Purpose

This document defines how the node-local agent enrolls, synchronizes, caches, persists, and serves effective configuration to local applications.

The agent is the core runtime delivery component. It is the boundary that transforms centrally managed versions into low-latency local reads.

## Agent Responsibilities

The V1 agent must:

1. Enroll with the control plane.
2. Authenticate itself on future calls.
3. Maintain awareness of current published versions relevant to its node.
4. Pull updated effective snapshots.
5. Keep active runtime data in memory.
6. Persist the last valid snapshot in encrypted local storage.
7. Serve local applications.
8. Report health and version state back to the control plane.

## Enrollment Flow

### Expected Flow

1. Operator creates or approves a managed node record.
2. Operator obtains a short-lived enrollment token from the control plane.
3. Agent starts with node identity metadata and enrollment token.
4. Agent calls the control plane enrollment endpoint.
5. Control plane validates node identity and enrollment token.
6. Control plane issues durable agent credentials and enrollment secret material.
7. Agent persists what it needs for future authenticated reconnects.

### Enrollment Output

At the end of enrollment, the agent should have:

- Its stable managed node identifier.
- A durable agent credential.
- Local secret material used for snapshot encryption.
- Initial sync metadata.

## Synchronization Model

### V1 Recommendation

Use an outbound agent connection for near-real-time invalidation, with polling fallback.

Preferred order:

1. Persistent outbound SignalR or WebSocket channel for invalidation events.
2. Fallback polling when a persistent channel is unavailable.

This achieves near-real-time updates while preserving operability in restricted networks.

## Publish Propagation Sequence

1. Control plane completes publish.
2. Control plane computes effective snapshots per target node and application.
3. Control plane stores accelerated snapshot material in Redis.
4. Control plane emits invalidation or version notifications.
5. Relevant agents receive notification.
6. Each agent fetches the new snapshot.
7. Each agent validates integrity and metadata.
8. Each agent activates or stages the snapshot according to rollout policy.

## Cache Hierarchy

The runtime hierarchy is intentionally local-first.

### Layer 1: Application Memory

The application or local provider should keep the active config in process memory.

### Layer 2: Agent Memory

The agent keeps the latest active effective snapshot in memory for local reads.

### Layer 3: Agent Encrypted Local Snapshot Store

The agent persists the last valid snapshot to survive restart and offline periods.

### Layer 4: Redis

Redis accelerates distribution and short-lived snapshot access during synchronization.

### Layer 5: PostgreSQL-backed Control Plane

Used to reconstruct canonical published state when Redis is unavailable or when auditability and history are required.

Applications must never rely on layers 4 or 5 for per-request runtime reads.

## Offline Behavior

The agent must continue serving the last valid snapshot while the control plane is unavailable.

### Product Rule

Offline use is allowed until local policy marks the snapshot as stale beyond the configured threshold.

### V1 Configuration Decision

The installation may set an offline maximum age. If not configured, the product may allow unlimited stale usage, but setup documentation must warn about the operational risk.

### Required Agent States

- `healthy`
- `degraded-offline`
- `stale-serving`
- `sync-failed`
- `not-enrolled`

## Rollout Policies

Every effective snapshot must carry a rollout policy that controls activation semantics.

### Supported Policies in V1

1. `immediate`: activate on successful fetch.
2. `scheduled`: activate at a specified time.
3. `manual-reload`: stage until a local reload action is triggered.
4. `restart-required`: write staged payload and mark restart needed.
5. `file-only`: project file update only and let the application decide reload behavior.

## Application Integration Modes

### Mode A: Local Runtime API

The application uses a local HTTP endpoint, optionally wrapped by a .NET configuration provider or SDK.

Benefits:

- Dynamic reload support.
- Consistent runtime API.
- Easy instrumentation.

### Mode B: JSON File Projection

The agent writes an effective JSON file to a configured path.

Benefits:

- Easy adoption for existing applications.
- Works well with `reloadOnChange`.
- Minimal application code changes.

## Local Runtime Guarantees

The agent must guarantee the following for local consumers:

1. Reads are served from local memory after activation.
2. The version identifier of the current snapshot is available.
3. The application can determine whether data is fresh, stale, or degraded.
4. Secret values are only returned to trusted local consumers according to integration mode and policy.

## Failure Handling

### Redis Unavailable

The agent falls back to control-plane fetch for published snapshots.

### Control Plane Unavailable

The agent serves the last valid local snapshot and marks itself degraded.

### Snapshot Validation Failure

The agent keeps the current active snapshot and reports the failure.

### Local Persistence Failure

The agent may continue serving from memory, but must raise health warnings because restart recovery is at risk.

## Performance Expectations

The design target is low-latency local reads and fast propagation of published changes.

What matters most:

- No central database lookup on hot application paths.
- Minimal local parsing after activation.
- Predictable behavior during partial outages.

## Implementation Notes

When coding begins, implement the agent with clearly separated components:

1. Enrollment client.
2. Control-plane sync client.
3. Snapshot validator.
4. Active in-memory state manager.
5. Local encrypted snapshot store.
6. Local runtime API host.
7. Optional file projection worker.

This separation will keep the runtime model testable and easier to evolve.