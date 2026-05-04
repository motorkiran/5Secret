# MVP Work Items

## Purpose

This document breaks the V1 implementation into small, independent work items that can each be completed and committed on their own.

The sequence below is deliberate. It aims to reduce rework by building foundations first, then domain behavior, then runtime delivery, then UI.

## Working Rule

Do not start a later item if an earlier dependency is still undefined or unstable.

## Current Status

Completed:

- SM-001 Initialize Solution Skeleton
- SM-002 Add Local Development Stack
- SM-003 Add Shared Configuration and Observability Baseline
- SM-004 Implement Installation Bootstrap and Root Owner
- SM-005 Implement Local Authentication and Password Storage
- SM-006 Implement RBAC Core Model and Permission Evaluator
- SM-007 Implement Audit Event Pipeline
- SM-008 Implement Environment Management
- SM-009 Implement Node Group and Managed Node Registry
- SM-010 Implement Application, Namespace, and Config Item Catalog
- SM-011 Implement appsettings Import Preview and Apply
- SM-012 Implement Draft Value Storage and Edit API
- SM-013 Implement Effective Preview Resolver
- SM-014 Implement Publish Pipeline and Immutable Version Records
- SM-015 Implement Diff and Rollback Support
- SM-016 Implement Agent Enrollment Flow
- SM-017 Implement Agent Heartbeat and Status Tracking
- SM-018 Implement Snapshot Distribution Through Redis With Fallback
- SM-019 Implement Agent Encrypted Local Snapshot Store
- SM-020 Implement Change Notification Channel
- SM-021 Implement Local Runtime API
- SM-022 Implement JSON File Projection Mode
- SM-023 Implement .NET Configuration Provider or Client SDK
- SM-024 Implement Bootstrap and Login UI
- SM-025 Implement Topology and Catalog Management UI
- SM-026 Implement Draft Editor, Publish, and Rollback UI
- SM-027 Implement Agent Status and Audit UI
- SM-028 End-to-End Happy Path and Packaging Baseline

Current focus:

- MVP backlog complete through SM-028.

## Foundation

### SM-001 Initialize Solution Skeleton

Goal:

Create the baseline solution and project boundaries.

Scope:

- Create the repository structure.
- Create the solution file.
- Create the initial .NET projects for control plane API, control plane application layer, domain layer, infrastructure layer, agent service, and shared contracts.
- Create the initial Angular web UI shell.

Out of scope:

- Business logic.
- Database schema.

Dependencies:

- None.

Acceptance criteria:

- The solution structure reflects the product architecture.
- All .NET projects and the Angular shell restore and build.
- Shared references do not create circular dependencies.

Verification:

- Build the solution successfully.
- Confirm startup placeholders exist for API, agent, and UI shell.

### SM-002 Add Local Development Stack

Goal:

Create a local development environment for control plane dependencies.

Scope:

- Add Docker Compose or equivalent local stack.
- Add PostgreSQL container configuration.
- Add Redis container configuration.
- Add environment configuration for local development.

Out of scope:

- Production deployment packaging.

Dependencies:

- SM-001.

Acceptance criteria:

- Developers can start PostgreSQL and Redis locally with one command.
- API and agent projects can reference expected connection settings.

Verification:

- Start the stack locally.
- Confirm PostgreSQL and Redis are reachable from the host environment.

### SM-003 Add Shared Configuration and Observability Baseline

Goal:

Establish consistent configuration loading, structured logging, and telemetry hooks.

Scope:

- Add common options binding patterns.
- Add structured logging baseline.
- Add OpenTelemetry placeholders for traces, metrics, and logs.
- Add correlation identifier middleware or equivalent.

Out of scope:

- Full dashboards.

Dependencies:

- SM-001.
- SM-002.

Acceptance criteria:

- API and agent use a shared configuration convention.
- Request correlation works in the API.
- Basic structured logs are emitted.

Verification:

- Start the API and agent.
- Confirm correlation identifiers appear in logs.

## Installation and Security Foundations

### SM-004 Implement Installation Bootstrap and Root Owner

Goal:

Support first-run initialization of installation settings and bootstrap owner creation.

Scope:

- Detect uninitialized installation state.
- Create installation record.
- Create bootstrap owner.
- Prevent a second bootstrap after initialization.

Out of scope:

- Password reset workflows.

Dependencies:

- SM-001.
- SM-002.

Acceptance criteria:

- First run allows bootstrap.
- Later runs reject repeat bootstrap.
- Bootstrap owner is stored securely.

Verification:

- Run bootstrap once successfully.
- Confirm repeat bootstrap is blocked.

### SM-005 Implement Local Authentication and Password Storage

Goal:

Enable secure local sign-in using username and password.

Scope:

- Add user model basics.
- Hash passwords with Argon2id.
- Implement login and logout.
- Add current-user endpoint.

Out of scope:

- MFA.
- LDAP.
- SSO.

Dependencies:

- SM-004.

Acceptance criteria:

- Valid users can sign in.
- Invalid credentials are rejected.
- Passwords are never stored reversibly.

Verification:

- Run authentication integration tests.
- Inspect stored password data for expected hash format.

### SM-006 Implement RBAC Core Model and Permission Evaluator

Goal:

Introduce roles, scoped assignments, and permission checks.

Scope:

- Add role and assignment entities.
- Add permission catalog.
- Add authorization evaluation service.
- Protect a first set of API endpoints.

Out of scope:

- Approval workflows.

Dependencies:

- SM-005.

Acceptance criteria:

- Scoped role assignments can be stored.
- Permission checks succeed and fail as expected.
- Secret reveal is a separate permission.

Verification:

- Run authorization unit tests for scope resolution.
- Run API tests with authorized and unauthorized users.

### SM-007 Implement Audit Event Pipeline

Goal:

Capture core security and configuration actions in a durable audit stream.

Scope:

- Add audit entity and storage.
- Write audit events for authentication, user changes, draft changes, publish, rollback, and secret reveal.
- Add correlation identifiers to audit records.

Out of scope:

- External audit export.

Dependencies:

- SM-005.
- SM-006.

Acceptance criteria:

- Sensitive actions generate audit records.
- Audit records include actor, action, target, and timestamp.
- Secret values are not written into audit payloads.

Verification:

- Trigger audited actions and inspect stored audit records.

## Topology and Catalog

### SM-008 Implement Environment Management

Goal:

Support CRUD for environments.

Scope:

- Add environment persistence.
- Add environment API endpoints.
- Add validation and uniqueness rules.

Out of scope:

- UI implementation.

Dependencies:

- SM-006.

Acceptance criteria:

- Environments can be created, listed, updated, and soft-deleted if supported.
- Authorization is enforced.

Verification:

- Run API tests for environment CRUD.

### SM-009 Implement Node Group and Managed Node Registry

Goal:

Support topology records for node groups and managed nodes.

Scope:

- Add node group and managed node entities.
- Add CRUD endpoints.
- Add status fields and last seen placeholders.

Out of scope:

- Agent enrollment.

Dependencies:

- SM-008.

Acceptance criteria:

- Node groups and nodes can be stored and queried.
- Nodes are linked to environments and optionally node groups.

Verification:

- Run API tests for node group and node CRUD.

### SM-010 Implement Application, Namespace, and Config Item Catalog

Goal:

Support the metadata catalog for managed applications.

Scope:

- Add application entity.
- Add namespace entity.
- Add config item entity with secret flag, type, and validation metadata.
- Add assignment model for application to node group or node.

Out of scope:

- Config values.

Dependencies:

- SM-008.
- SM-009.

Acceptance criteria:

- Applications, namespaces, and config items can be managed.
- Config items support secret and non-secret classification.

Verification:

- Run API tests for catalog CRUD.

### SM-011 Implement appsettings Import Preview and Apply

Goal:

Allow customers to import existing appsettings-style JSON into the catalog and draft model.

Scope:

- Parse JSON input.
- Discover namespaces and keys.
- Preview import result.
- Apply approved import into catalog metadata and draft values.

Out of scope:

- Publishing imported values automatically.

Dependencies:

- SM-010.

Acceptance criteria:

- A valid appsettings payload can be previewed.
- Operators can apply the preview into the system.
- Conflicts are reported clearly.

Verification:

- Run import tests with representative JSON samples.

## Drafts, Versions, and Publishing

### SM-012 Implement Draft Value Storage and Edit API

Goal:

Allow operators to create and modify draft values at supported scopes.

Scope:

- Add draft value persistence.
- Add scope model for application default, environment, node group, node, and emergency override.
- Add create, update, delete endpoints for draft values.

Out of scope:

- Published versions.

Dependencies:

- SM-010.

Acceptance criteria:

- Draft values can be managed independently of published versions.
- Secret draft values are encrypted at rest.
- Draft changes are audited.

Verification:

- Run API tests for draft CRUD across multiple scopes.

### SM-013 Implement Effective Preview Resolver

Goal:

Compute the resolved effective value set before publish.

Scope:

- Apply override precedence rules.
- Resolve conflicts deterministically.
- Return effective preview for a given environment, node, and application.

Out of scope:

- Immutable version storage.

Dependencies:

- SM-012.

Acceptance criteria:

- Effective preview reflects the documented precedence order.
- Preview clearly indicates source scope for each resolved value.

Verification:

- Run resolver unit tests with overlapping scope scenarios.

### SM-014 Implement Publish Pipeline and Immutable Version Records

Goal:

Turn draft state into immutable published versions.

Scope:

- Add publish operation entity.
- Add published version entity.
- Compute content hashes.
- Store immutable published records.

Out of scope:

- Agent synchronization.

Dependencies:

- SM-013.

Acceptance criteria:

- Publish creates immutable version records.
- Publish is auditable.
- Publish does not mutate historical versions.

Verification:

- Run publish integration tests.
- Confirm repeated publishes create new versions, not overwrites.

### SM-015 Implement Diff and Rollback Support

Goal:

Allow operators to compare versions and roll back safely.

Scope:

- Add diff endpoint for version comparison.
- Add rollback command that republishes a previous version as the new active target.
- Audit rollback actions.

Out of scope:

- Agent rollout UX.

Dependencies:

- SM-014.

Acceptance criteria:

- Differences between versions are visible.
- Rollback creates a new publish event instead of mutating old records.

Verification:

- Run diff tests.
- Publish version A, version B, then roll back and verify the resulting effective content.

## Agent and Distribution

### SM-016 Implement Agent Enrollment Flow

Goal:

Allow a node-local agent to enroll and obtain durable identity.

Scope:

- Add enrollment token workflow.
- Add agent enrollment endpoint.
- Persist agent credentials and node binding.

Out of scope:

- Continuous sync.

Dependencies:

- SM-009.
- SM-014.

Acceptance criteria:

- A new node can be enrolled once.
- Reuse or invalid enrollment attempts are rejected.
- Enrollment is audited.

Verification:

- Run enrollment integration tests.

### SM-017 Implement Agent Heartbeat and Status Tracking

Goal:

Give operators visibility into node health and freshness.

Scope:

- Add heartbeat endpoint.
- Store last seen, current version, agent version, and health state.
- Expose status query API.

Out of scope:

- Change notifications.

Dependencies:

- SM-016.

Acceptance criteria:

- Agents can report heartbeat.
- Control plane exposes last seen and health status.
- Missing heartbeat can be surfaced as degraded state.

Verification:

- Simulate heartbeat and missed heartbeat transitions.

### SM-018 Implement Snapshot Distribution Through Redis With Fallback

Goal:

Use Redis to accelerate distribution of published snapshots while keeping fallback to canonical state.

Scope:

- Store published effective snapshot payloads or metadata in Redis.
- Add control-plane fetch fallback when Redis is unavailable.
- Add snapshot hash validation.

Out of scope:

- Local encrypted persistence.

Dependencies:

- SM-014.
- SM-017.

Acceptance criteria:

- Agents can fetch target snapshots from Redis-backed flow.
- Agents can still fetch through fallback path when Redis is unavailable.

Verification:

- Run synchronization tests with Redis available and unavailable.

### SM-019 Implement Agent Encrypted Local Snapshot Store

Goal:

Ensure agents survive restart and offline periods.

Scope:

- Add local encrypted snapshot persistence.
- Derive local encryption key from enrollment secret and local salt.
- Load last valid snapshot on startup.

Out of scope:

- Advanced recovery tooling.

Dependencies:

- SM-016.
- SM-018.

Acceptance criteria:

- Agent can restart and restore the last valid snapshot.
- Snapshot storage is encrypted at rest.
- Corrupted snapshot files do not silently become active.

Verification:

- Restart agent after sync and verify recovery.
- Corrupt persisted data and verify safe failure behavior.

### SM-020 Implement Change Notification Channel

Goal:

Push near-real-time invalidation events to agents.

Scope:

- Add outbound agent connection mechanism.
- Send version invalidation messages on publish or rollback.
- Add polling fallback.

Out of scope:

- Advanced rollout orchestration.

Dependencies:

- SM-017.
- SM-018.

Acceptance criteria:

- Agents receive publish invalidation without manual refresh.
- Agents can fall back to polling when persistent connection is unavailable.

Verification:

- Publish a change and verify agent pickup through both primary and fallback paths.

### SM-021 Implement Local Runtime API

Goal:

Serve effective snapshots to applications on the same machine.

Scope:

- Add local runtime snapshot endpoint.
- Add version and health endpoint.
- Return active state and freshness metadata.

Out of scope:

- File projection mode.

Dependencies:

- SM-019.
- SM-020.

Acceptance criteria:

- Local applications can read the current snapshot.
- Runtime responses expose version and health state.

Verification:

- Run local agent integration tests against runtime endpoints.

### SM-022 Implement JSON File Projection Mode

Goal:

Support legacy or low-touch application integration through projected files.

Scope:

- Write effective JSON files to configured local paths.
- Honor rollout policy when activating projected files.
- Expose projection state in agent health.

Out of scope:

- Language-specific SDK features.

Dependencies:

- SM-021.

Acceptance criteria:

- Agent can write and update projected JSON.
- File updates reflect the active snapshot version.

Verification:

- Run file projection tests and confirm versioned updates on disk.

### SM-023 Implement .NET Configuration Provider or Client SDK

Goal:

Provide a first-class .NET integration path for applications that want runtime API access and in-process caching.

Scope:

- Add a .NET client package or internal provider.
- Cache local runtime data in process.
- Support reload signaling or periodic refresh based on the chosen model.

Out of scope:

- Non-.NET client libraries.

Dependencies:

- SM-021.

Acceptance criteria:

- A sample .NET application can consume config through the local agent.
- The provider exposes active version metadata.

Verification:

- Run a sample application smoke test.

## UI Slices

### SM-024 Implement Bootstrap and Login UI

Goal:

Provide the first usable operator-facing workflow.

Scope:

- Add bootstrap screen.
- Add login screen.
- Add authenticated layout shell.

Out of scope:

- Resource management pages.

Dependencies:

- SM-004.
- SM-005.

Acceptance criteria:

- A fresh installation can be bootstrapped through the UI.
- An existing user can sign in and sign out.

Verification:

- Run end-to-end UI smoke test for bootstrap and login.

### SM-025 Implement Topology and Catalog Management UI

Goal:

Expose environments, node groups, nodes, applications, namespaces, and config item catalog in the UI.

Scope:

- Add list and detail pages.
- Add CRUD forms.
- Show scope relationships clearly.

Out of scope:

- Draft editing and publish UX.

Dependencies:

- SM-008.
- SM-009.
- SM-010.
- SM-024.

Acceptance criteria:

- Operators can manage topology and catalog records through the UI.
- Secret flags and validation metadata are visible.

Verification:

- Run UI flow tests for topology and catalog CRUD.

### SM-026 Implement Draft Editor, Publish, and Rollback UI

Goal:

Expose the core configuration workflow in the UI.

Scope:

- Add draft editing screens.
- Add effective preview UI.
- Add diff and publish flow.
- Add rollback flow.

Out of scope:

- Four-eyes approval.

Dependencies:

- SM-012.
- SM-013.
- SM-014.
- SM-015.
- SM-024.

Acceptance criteria:

- Operators can edit drafts, preview changes, publish, and roll back from the UI.
- Secret values remain masked by default.

Verification:

- Run end-to-end UI tests for the main draft and publish workflow.

### SM-027 Implement Agent Status and Audit UI

Goal:

Expose runtime freshness and audit visibility to operators.

Scope:

- Add agent status list and detail views.
- Add audit event list and filtering.
- Show degraded, stale, and sync-failed states.

Out of scope:

- External audit export.

Dependencies:

- SM-007.
- SM-017.
- SM-024.

Acceptance criteria:

- Operators can inspect node health and audit history from the UI.

Verification:

- Run UI tests for status and audit pages.

## Hardening and Delivery Readiness

### SM-028 End-to-End Happy Path and Packaging Baseline

Goal:

Prove the full V1 slice in a repeatable local and installable scenario.

Scope:

- Add sample development setup instructions.
- Add a full happy-path scenario covering bootstrap, node enrollment, import, draft edit, publish, sync, local consumption, and rollback.
- Add first packaging baseline for self-hosted installation.

Out of scope:

- High availability.
- External integrations.

Dependencies:

- SM-001 through SM-027.

Acceptance criteria:

- A clean environment can run the full V1 flow.
- The documented scenario matches actual behavior.

Verification:

- Execute the documented happy path end to end.

## Suggested Milestone Grouping

To keep momentum high, execute the work in these groups:

1. Foundation and security: SM-001 through SM-007.
2. Topology and catalog: SM-008 through SM-011.
3. Draft, publish, and rollback: SM-012 through SM-015.
4. Agent runtime delivery: SM-016 through SM-023.
5. UI slices: SM-024 through SM-027.
6. Final proof and packaging: SM-028.

## Resume Guidance

If the project pauses, resume by checking:

1. Which `SM-###` item was the last completed one.
2. Whether any architecture assumptions changed.
3. Whether the next item still has all dependencies satisfied.

This file is the primary implementation queue for V1.