# API Contracts

## Purpose

This document defines the conceptual API surface for the control plane and the node-local agent.

It is not intended to replace a future OpenAPI definition, but it defines the resource boundaries and endpoint groups that the implementation should follow.

## General API Rules

1. Version management APIs under `/api/v1`.
2. Use resource-oriented endpoints.
3. Return masked secret data by default.
4. Use pagination for list endpoints.
5. Include stable identifiers and version metadata in responses.
6. Use explicit actions for publish, rollback, reveal, and import preview.

## Control Plane API Areas

### Authentication

- `POST /api/v1/auth/bootstrap`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/change-password`
- `GET /api/v1/auth/me`

### Users and Roles

- `GET /api/v1/users`
- `POST /api/v1/users`
- `PATCH /api/v1/users/{userId}`
- `GET /api/v1/roles`
- `POST /api/v1/roles`
- `POST /api/v1/role-assignments`
- `DELETE /api/v1/role-assignments/{assignmentId}`

### Environment and Topology

- `GET /api/v1/environments`
- `POST /api/v1/environments`
- `PATCH /api/v1/environments/{environmentId}`
- `DELETE /api/v1/environments/{environmentId}`
- `GET /api/v1/node-groups`
- `POST /api/v1/node-groups`
- `GET /api/v1/nodes`
- `POST /api/v1/nodes`
- `PATCH /api/v1/nodes/{nodeId}`

### Applications and Namespaces

- `GET /api/v1/applications`
- `POST /api/v1/applications`
- `GET /api/v1/namespaces`
- `POST /api/v1/namespaces`
- `POST /api/v1/application-assignments`

### Config Metadata and Drafts

- `GET /api/v1/config-items`
- `POST /api/v1/config-items`
- `PATCH /api/v1/config-items/{configItemId}`
- `GET /api/v1/drafts`
- `PUT /api/v1/drafts/values/{draftValueId}`
- `DELETE /api/v1/drafts/values/{draftValueId}`

### Imports

- `POST /api/v1/imports/appsettings/preview`
- `POST /api/v1/imports/appsettings/apply`

### Publish, Diff, and Rollback

- `GET /api/v1/publishes`
- `POST /api/v1/publishes`
- `GET /api/v1/published-versions`
- `GET /api/v1/published-versions/{versionId}/diff`
- `POST /api/v1/published-versions/{versionId}/rollback`

### Effective Snapshots and Agent Operations

- `GET /api/v1/effective-snapshots`
- `GET /api/v1/agents`
- `GET /api/v1/agents/{agentId}/status`
- `POST /api/v1/agents/{agentId}/refresh`

### Audit

- `GET /api/v1/audit-events`
- `GET /api/v1/audit-events/{eventId}`

## Agent Control API

These endpoints are consumed by the node-local agent against the control plane.

- `POST /api/v1/agent/enroll`
- `POST /api/v1/agent/heartbeat`
- `POST /api/v1/agent/sync/check`
- `GET /api/v1/agent/snapshots/{snapshotId}`

The exact transport for invalidation notifications can be SignalR, WebSocket, or server-sent events, but the contract must include version metadata and target identity.

## Node-Local Runtime API

These endpoints are exposed by the local agent to applications on the same machine.

- `GET /runtime/v1/applications/{applicationSlug}/snapshot`
- `GET /runtime/v1/applications/{applicationSlug}/version`
- `GET /runtime/v1/applications/{applicationSlug}/health`
- `POST /runtime/v1/applications/{applicationSlug}/reload-ack`

If file projection mode is used, these endpoints may be optional for that application.

## Response Shape Guidance

Responses should be explicit and predictable.

### Example Snapshot Metadata Response

```json
{
  "application": "trading-api",
  "environment": "production",
  "node": "prod-node-17",
  "version": 42,
  "snapshotHash": "sha256:...",
  "state": "healthy",
  "rolloutPolicy": "immediate",
  "updatedAt": "2026-04-23T12:34:56Z"
}
```

### Example Local Runtime Snapshot Response

```json
{
  "application": "trading-api",
  "version": 42,
  "state": "healthy",
  "values": {
    "ConnectionStrings:Primary": "...",
    "Trading:ThrottleLimit": 500,
    "Trading:EnableRiskChecks": true
  }
}
```

## Error Model

Errors should contain:

- `code`
- `message`
- `correlationId`
- `details` when safe to expose

Avoid returning implementation exceptions directly.

## Concurrency Guidance

When editing drafts or updating mutable resources, the implementation should consider optimistic concurrency through row versions, timestamps, or ETags.

## API Security Guidance

1. Do not return plaintext secrets unless explicitly requested and authorized.
2. Never log secret payloads.
3. Tie all management requests to authenticated user context.
4. Tie all agent requests to authenticated agent identity.