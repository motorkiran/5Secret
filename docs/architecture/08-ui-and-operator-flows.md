# UI and Operator Flows

## Purpose

This document defines the minimum operator experience required for V1. The UI should expose the product model clearly rather than acting as a generic table editor.

## UI Design Principles

1. Show effective context clearly: environment, node group, node, application, and namespace.
2. Make secret handling explicit: masked by default, reveal through deliberate action.
3. Separate draft editing from publish actions.
4. Make version history, diffs, and rollback discoverable.
5. Show agent health and freshness state near runtime-related workflows.

## Core Screens

### Bootstrap and Login

Required capabilities:

- Create bootstrap owner on first run.
- Sign in with local credentials.
- Force password change if policy requires it later.

### Dashboard

Suggested content:

- Environment summary.
- Node health summary.
- Pending draft changes.
- Latest published versions.
- Recent audit events.

### Environment and Topology Management

Operators must be able to:

- Create environments.
- Create node groups.
- Register or approve nodes.
- See last seen and agent version.
- See degraded and stale nodes.

### Application and Namespace Management

Operators must be able to:

- Create applications.
- Assign applications to node groups or nodes.
- Create namespaces.
- View config items grouped by namespace.

### Config Item Catalog

Operators must be able to:

- Create config items.
- Mark items as secret or plain configuration.
- Set data type and validation rules.
- Add descriptions and documentation.

### Draft Editor

Operators must be able to:

- Filter by environment, application, node group, node, and namespace.
- Edit draft values.
- See inheritance source.
- Add overrides.
- Remove overrides.
- Preview effective values before publish.

### Import Workflow

Operators must be able to:

- Upload appsettings-style JSON.
- Preview discovered namespaces and keys.
- Review conflicts.
- Apply import into draft state.

### Publish Workflow

Operators must be able to:

- Review current draft changes.
- Compare against the currently published version.
- Enter a change note.
- Choose or review rollout policy.
- Publish.

### Version History and Rollback

Operators must be able to:

- Browse published versions.
- View diffs between versions.
- Roll back to a previous version.
- See who published each version.

### Audit Viewer

Operators must be able to:

- Filter by actor, action, environment, application, and time.
- Inspect secret reveal events.
- Inspect publish and rollback events.

### User and Role Management

Operators with permission must be able to:

- Create users.
- Disable users.
- Reset passwords.
- Assign roles scoped to resources.

## Key User Journeys

### Journey 1: First-Time Setup

1. Install the stack.
2. Open UI.
3. Create bootstrap owner.
4. Define first environment.
5. Define first node group.
6. Register first node agent.
7. Create first application and namespace.
8. Import initial appsettings file.
9. Publish first version.

### Journey 2: Daily Config Change

1. Sign in.
2. Navigate to application draft editor.
3. Modify one or more draft values.
4. Preview effective impact.
5. Publish with change note.
6. Verify node and application update status.

### Journey 3: Incident Rollback

1. Find affected application and environment.
2. Open version history.
3. Compare recent versions.
4. Trigger rollback.
5. Confirm agent synchronization status.

## UX Warnings

The UI must avoid the following mistakes:

1. Hiding scope context while editing values.
2. Making secret reveal visually identical to normal read.
3. Mixing draft and published states in one ambiguous table.
4. Hiding degraded or stale agent conditions.
5. Requiring operators to understand raw database-style relationships.

## UI Deliverable Priority

If implementation capacity is limited, prioritize these screens first:

1. Bootstrap and login.
2. Environment, node group, and node management.
3. Application, namespace, and config item catalog.
4. Draft editor.
5. Publish diff and rollback.
6. Agent status screen.
7. Audit viewer.