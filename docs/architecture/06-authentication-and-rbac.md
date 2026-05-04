# Authentication and RBAC

## Purpose

This document defines the V1 identity and authorization model for SecretManager.

The product needs more than simple CRUD permissions. It must distinguish between viewing, revealing, editing, publishing, deleting, and administering access.

## Authentication Model

### V1 Decision

V1 supports local username and password authentication only.

### Requirements

1. Bootstrap owner creation during installation.
2. Password hashing with Argon2id.
3. Account enable and disable support.
4. Password change workflow.
5. Session expiration and logout.

### Deferred Authentication Features

- LDAP
- SSO
- MFA
- Service accounts
- Personal access tokens

These are intentionally deferred but the authorization model should not block them later.

## Authorization Principles

1. Authorization is deny by default.
2. Roles are templates for permission bundles.
3. Assignments can be scoped.
4. Secret reveal is separate from plain read.
5. Publish is separate from draft edit.
6. Audit viewing is separate from configuration management.

## Resource Scopes

Permissions may be granted at these scopes:

1. Installation
2. Environment
3. Node Group
4. Managed Node
5. Application
6. Namespace
7. Config Item

Higher scope grants may cascade downward unless explicitly constrained by future policy logic.

## Permission Verbs

At minimum, V1 should support these verbs:

- `users.read`
- `users.write`
- `roles.read`
- `roles.write`
- `environments.read`
- `environments.write`
- `nodeGroups.read`
- `nodeGroups.write`
- `nodes.read`
- `nodes.write`
- `applications.read`
- `applications.write`
- `namespaces.read`
- `namespaces.write`
- `config.readMasked`
- `config.revealSecret`
- `config.writeDraft`
- `config.deleteDraft`
- `config.publish`
- `config.rollback`
- `audit.read`
- `agents.read`
- `agents.write`

## Recommended Default Roles

### Bootstrap Owner

Full installation-wide permissions. This role is used carefully and should be rare.

### Security Administrator

Manages users, roles, reveal permissions, and security-sensitive controls.

### Configuration Administrator

Manages environments, nodes, applications, config metadata, drafts, publish, and rollback.

### Environment Operator

Manages a limited environment or node subset. Can edit drafts in assigned scopes and optionally publish if granted.

### Auditor

Read-only access to configuration metadata and audit history. No secret reveal and no write access.

### Reader

Masked read access to assigned scopes with no mutation or reveal.

## Recommended Evaluation Rules

1. Resolve user role assignments for the requested resource.
2. Determine whether a matching scope grant exists.
3. Require explicit permission for secret reveal.
4. Reject access if no grant exists.
5. Audit sensitive decisions such as reveal, publish, rollback, and user administration.

## V1 Simplifications

V1 intentionally excludes:

- Approval workflows.
- Conditional policies based on time or network.
- Attribute-based access control.
- Just-in-time elevation.

However, the permission catalog and scope model should make later expansion possible.

## Role Assignment Model

Suggested assignment fields:

- `userId`
- `roleId`
- `scopeType`
- `scopeId`
- `createdBy`
- `createdAt`
- `expiresAt` (optional for future use)

## Secret Reveal Policy

The UI and API should default to masked values. Reveal must require:

1. `config.revealSecret` on the relevant scope.
2. Explicit operator action.
3. Audit record creation.

## Publish and Rollback Controls

Publishing and rollback must be treated as privileged actions. A user who can edit drafts should not automatically gain publish or rollback permissions.

## Future-Compatible Extensions

The RBAC model should leave room for later additions:

- Service principals.
- Personal access tokens.
- Approval workflow roles.
- Emergency break-glass roles.
- Temporary role elevation.