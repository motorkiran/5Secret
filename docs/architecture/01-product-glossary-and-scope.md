# Product Glossary and Scope

## Problem Statement

Customers run dozens of microservices across many machines. Each machine may require different runtime settings, secrets, or appsettings overrides. Managing those values directly in files, environment variables, or ad hoc scripts becomes error-prone, hard to audit, and difficult to roll back.

SecretManager addresses that problem by providing a self-hosted product that centralizes management while delivering effective configuration locally on each machine.

## Product Goals

1. Provide a safe way to manage both plain configuration and secrets.
2. Support many applications across many managed nodes within one customer installation.
3. Deliver runtime values with low latency and minimal application overhead.
4. Support shared defaults with environment, node group, and node-specific overrides.
5. Provide operator visibility through version history, diffs, audit logs, and health status.
6. Allow customers to adopt the product incrementally, including importing existing appsettings data.

## Non-Goals for V1

1. Secret generation or automatic rotation connectors.
2. Multi-customer SaaS isolation.
3. Federated identity integration such as LDAP or SSO.
4. Distributed multi-region active-active control plane.
5. File, binary blob, or certificate authority lifecycle management.

## Personas

### Bootstrap Owner

The first privileged operator created during installation. Responsible for initial setup, security, and top-level administration.

### Security Administrator

Manages user access, password policies, reveal permissions, and sensitive operational controls.

### Configuration Administrator

Creates applications, namespaces, config items, draft changes, and published releases.

### Environment Operator

Manages a subset of environments or nodes and handles daily operational changes.

### Auditor

Reviews change history, access history, and operational events but does not modify configuration.

### Application Runtime

Consumes configuration through the node-local agent or projected files and should not need to contact the control plane directly.

## Core Terms

### Installation

One self-hosted deployment of SecretManager for a single customer.

### Environment

A top-level deployment dimension such as development, test, stage, or production.

### Node Group

A logical grouping of managed nodes that should share common application assignments or configuration defaults.

### Managed Node

One server, VM, container host, workstation, or edge device that runs the local SecretManager agent.

### Application

A deployable service or microservice whose runtime configuration is managed by SecretManager.

### Namespace

A logical grouping inside an application, usually mapped to a configuration section or bounded area such as `ConnectionStrings`, `Payments`, or `TradingEngine`.

### Config Item

One configuration key with metadata such as data type, validation rules, secret flag, masking policy, and documentation.

### Draft

An editable working state that contains pending changes not yet active in runtime delivery.

### Published Version

An immutable version that has passed through the publish workflow and is available for runtime delivery.

### Effective Snapshot

The resolved runtime representation of configuration for a specific environment, node, and application after all inheritance and overrides are applied.

### Rollout Policy

The rule that controls when a published snapshot becomes active in the local application context.

## Scope Boundaries

### In Scope

- Managing application configuration and secrets.
- Versioning and rollback.
- Environment, node group, node, application, and namespace hierarchy.
- UI and API for CRUD and operational workflows.
- Node-local runtime delivery.
- Local offline cache on the agent.
- Audit logging for configuration and access events.

### Out of Scope in V1

- Acting as a certificate authority.
- Storing large binary assets.
- Multi-tenant billing or commercial account management.
- Cross-node service discovery.
- Secret scanning in source control.

## Functional Scope Summary

The V1 product must support the following end-to-end user journeys:

1. Install the product and create the bootstrap owner.
2. Define environments and node groups.
3. Register managed nodes through local agents.
4. Create applications and namespaces.
5. Import or define config items.
6. Edit draft values.
7. Publish a version.
8. Observe agent synchronization.
9. Roll back a version.
10. Review audit history.

## Product Principles

1. Runtime delivery must favor local reads over central lookups.
2. Sensitive actions must be explainable, auditable, and authorization-aware.
3. Operators should manage effective configuration, not raw duplication.
4. The system must distinguish between editing, publishing, revealing, and deleting.
5. Any behavior that can create downtime should be explicit and policy-driven.

## Recommended Naming Conventions

Use the following vocabulary consistently across code, documentation, APIs, and UI:

- `installation`
- `environment`
- `nodeGroup`
- `managedNode`
- `application`
- `namespace`
- `configItem`
- `draft`
- `publishedVersion`
- `effectiveSnapshot`
- `rolloutPolicy`

Avoid using `tenant` to mean a node or server. In this product, that term introduces unnecessary ambiguity.