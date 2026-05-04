# Domain and Data Model

## Modeling Strategy

The domain model must separate three concerns:

1. Resource hierarchy and metadata.
2. Draft and versioned configuration state.
3. Effective runtime delivery state.

Avoid collapsing these into one table or one blob. That approach becomes hard to audit, validate, and evolve.

## Core Resource Hierarchy

The base hierarchy is:

`Installation -> Environment -> Node Group -> Managed Node -> Application -> Namespace -> Config Item`

Not every relationship is strictly physical. Node groups are logical, applications may be assigned to multiple nodes, and namespaces are organizational.

## Core Entities

### Environment

Represents a high-level lifecycle boundary such as development, test, stage, or production.

Suggested fields:

- `id`
- `name`
- `slug`
- `description`
- `isProtected`
- `createdAt`
- `updatedAt`

### Node Group

Represents a reusable operational grouping.

Suggested fields:

- `id`
- `environmentId`
- `name`
- `slug`
- `description`
- `createdAt`
- `updatedAt`

### Managed Node

Represents one machine running a local agent.

Suggested fields:

- `id`
- `environmentId`
- `nodeGroupId`
- `name`
- `hostname`
- `platform`
- `status`
- `lastSeenAt`
- `agentVersion`
- `rolloutPolicyDefault`
- `createdAt`
- `updatedAt`

### Application

Represents one microservice or deployable unit.

Suggested fields:

- `id`
- `name`
- `slug`
- `description`
- `defaultIntegrationMode`
- `createdAt`
- `updatedAt`

### Application Assignment

Maps applications to node groups or nodes.

Suggested fields:

- `id`
- `applicationId`
- `environmentId`
- `nodeGroupId`
- `managedNodeId`
- `enabled`
- `createdAt`

### Namespace

Represents a logical section of an application configuration.

Suggested fields:

- `id`
- `applicationId`
- `name`
- `path`
- `description`
- `createdAt`
- `updatedAt`

### Config Item

Represents a configuration key and its metadata.

Suggested fields:

- `id`
- `applicationId`
- `namespaceId`
- `key`
- `fullPath`
- `valueType`
- `isSecret`
- `isRequired`
- `defaultRolloutPolicy`
- `validationSchemaJson`
- `description`
- `createdAt`
- `updatedAt`

## Scope and Override Model

The product needs reuse plus specificity. To avoid duplication, values are stored at scopes.

### Recommended Scope Order

From lowest precedence to highest precedence:

1. Application default
2. Environment override
3. Node group override
4. Managed node override
5. Emergency override

If a later scope defines the same config item, it replaces the earlier one in the effective snapshot.

## Draft Editing Model

Drafts should be modeled explicitly.

### Draft Value

Represents a pending value for a config item at a given scope.

Suggested fields:

- `id`
- `configItemId`
- `scopeType`
- `scopeId`
- `valueCiphertextOrPlain`
- `isSecret`
- `changeNote`
- `updatedByUserId`
- `updatedAt`

### Why Drafts Matter

Drafts allow operators to make multiple related changes before publishing a coherent version.

## Publish Model

Publishing should produce immutable version records.

### Publish Operation

Represents a publish event.

Suggested fields:

- `id`
- `environmentId`
- `initiatedByUserId`
- `changeSummary`
- `createdAt`
- `completedAt`
- `status`

### Published Version

Represents an immutable version for a target slice.

Suggested fields:

- `id`
- `publishOperationId`
- `environmentId`
- `applicationId`
- `versionNumber`
- `contentHash`
- `publishedByUserId`
- `publishedAt`
- `supersedesVersionId`

### Effective Snapshot

Represents the resolved payload delivered to a node for a specific application.

Suggested fields:

- `id`
- `publishedVersionId`
- `environmentId`
- `managedNodeId`
- `applicationId`
- `integrationMode`
- `rolloutPolicy`
- `snapshotHash`
- `payloadCiphertextOrSerializedJson`
- `createdAt`

## Immutable Versus Mutable Data

### Mutable

- Resource metadata.
- Draft values.
- Role assignments.
- Agent health state.

### Immutable

- Published versions.
- Effective snapshots tied to a version.
- Audit events.

This distinction must remain clear in code and schema design.

## Audit Model

At minimum, the audit model should support:

- Actor identity.
- Action type.
- Target resource type.
- Target resource identifier.
- Before and after summary where safe.
- Timestamp.
- Correlation identifier.
- Request origin metadata.

## Version Numbering

V1 can use monotonically increasing integers per `(environment, application)` pair. SemVer-like labels can be added later if needed, but integer version ordering keeps rollback and diff logic simple.

## Import Model

The system should support importing existing appsettings-style JSON into:

1. Applications.
2. Namespaces.
3. Config items.
4. Initial draft values.

Imports should be reviewable before publish.

## Deletion Strategy

Avoid hard deletion for published data.

Recommended behavior:

- Soft-delete metadata resources when possible.
- Mark draft overrides as removed.
- Keep published versions and audit records immutable.

## Schema Evolution Guidance

As implementation begins, start with a normalized relational schema. If some snapshot payloads later benefit from JSONB storage, use it intentionally for snapshot materialization rather than as the primary model for all domain objects.