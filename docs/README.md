# SecretManager Documentation Blueprint

## Purpose

This documentation set defines the target product, architectural boundaries, security model, delivery plan, and future roadmap for SecretManager.

SecretManager is not modeled as a generic secret vault. It is designed as a self-hosted configuration and secret control plane for distributed .NET-centric microservice environments that run across many servers.

The product will provide:

- A central control plane for operators built on ASP.NET Core with an Angular-based management UI.
- A node-local agent on every managed machine.
- Versioned configuration and secret management.
- Fine-grained authorization.
- Runtime delivery patterns for fast reads and safe rollouts.

## Frozen Decisions

The following decisions are considered frozen unless a later Architecture Decision Record explicitly changes them:

1. The product is self-hosted and deployed per customer installation.
2. The management UI talks only to the control plane backend.
3. Every managed machine runs its own node-local agent.
4. The node-local agent serves only applications on the same machine.
5. PostgreSQL is the system of record.
6. Redis is a delivery accelerator and coordination layer, not the source of truth.
7. Runtime application reads must come from process memory or a node-local source, not from PostgreSQL or Redis per request.
8. The product supports environments such as development, test, stage, and production.
9. The product supports shared configuration with node-specific overrides.
10. V1 ships with a single control plane instance, designed in a way that can later evolve toward high availability.
11. V1 supports local username and password authentication only.
12. V1 does not include four-eyes approval, service accounts, SSO, LDAP, or audit export integrations.
13. V1 targets key-value and JSON configuration payloads aligned with .NET appsettings usage.
14. The implementation baseline uses .NET 10 for backend services and Angular for the web UI.

## Product Shape

SecretManager is split into two major planes:

- Control plane: operator-facing APIs, UI, authorization, versioning, audit logging, snapshot computation, and publish orchestration.
- Data plane: node-local agent, runtime delivery, local caching, encrypted offline snapshot storage, and application integration.

## Documentation Map

Read the documents in the following order when starting implementation:

1. [Product Glossary and Scope](architecture/01-product-glossary-and-scope.md)
2. [System Architecture](architecture/02-system-architecture.md)
3. [Security and Cryptography](architecture/03-security-and-cryptography.md)
4. [Domain and Data Model](architecture/04-domain-and-data-model.md)
5. [Agent Sync and Runtime Behavior](architecture/05-agent-sync-and-runtime.md)
6. [Authentication and RBAC](architecture/06-authentication-and-rbac.md)
7. [API Contracts](architecture/07-api-contracts.md)
8. [UI and Operator Flows](architecture/08-ui-and-operator-flows.md)
9. [Local Happy Path and Packaging Baseline](architecture/09-local-happy-path-and-packaging-baseline.md)
10. [Local Installation and Operator Guide](architecture/10-local-installation-and-operator-guide.md)
11. [Server Deployment and Configuration Guide](architecture/11-server-deployment-and-configuration.md)
12. [Backlog Execution Rules](backlog/00-execution-rules.md)
13. [MVP Work Items](backlog/01-mvp-work-items.md)
14. [Phase 2 and Future Roadmap](backlog/02-phase-2-and-future-roadmap.md)

## Design Principles

1. Model the product as a control plane plus a data plane, not as a single API with caches.
2. Keep PostgreSQL authoritative for writes, versions, and auditability.
3. Optimize runtime reads by pushing effective configuration close to the application.
4. Separate configuration metadata, configuration values, and published runtime snapshots.
5. Treat secrets differently from plain configuration even when stored in the same product.
6. Prefer immutable published snapshots over in-place mutation of active runtime state.
7. Design for safe rollback, explainability, and operator visibility before designing for scale-out.
8. Keep every backlog item small enough to land as an isolated commit with measurable acceptance criteria.

## Recommended Reading Paths

### Path A: Architecture and Product Design

Read documents 1 through 9 in order.

### Path B: Implementation Planning

Read documents 9 through 13 after documents 1 through 4.

### Path C: Re-entry After a Break

If the project pauses and resumes later, start with:

1. This file.
2. [MVP Work Items](backlog/01-mvp-work-items.md).
3. [Phase 2 and Future Roadmap](backlog/02-phase-2-and-future-roadmap.md).

## High-Level Lifecycle

1. Operator defines environments, node groups, nodes, applications, namespaces, and configuration items.
2. Operator edits draft values and metadata.
3. Operator publishes a version.
4. Control plane computes effective runtime snapshots.
5. Control plane stores the immutable version and distributes invalidation signals.
6. Node agents pull the new snapshot, refresh memory, and persist an encrypted local snapshot.
7. Applications reload immediately, on schedule, or on restart depending on rollout policy.

## What V1 Must Prove

V1 is successful if it proves the following capabilities end to end:

- Bootstrap a secure installation.
- Model environments, nodes, applications, and configuration scopes.
- Import appsettings-style configuration.
- Store secrets securely.
- Publish immutable versions.
- Deliver effective configuration to node-local agents quickly.
- Support offline operation from encrypted local snapshots.
- Provide usable CRUD, diff, publish, rollback, and audit workflows.

## Deferred but Planned

The roadmap intentionally defers the following items to later phases:

- High-availability control plane topology.
- Service accounts and personal access tokens.
- Four-eyes approval workflow.
- Secret rotation connectors.
- SSO and LDAP integration.
- Audit export to external systems.
- External KMS and HSM integration.

These items are documented in [Phase 2 and Future Roadmap](backlog/02-phase-2-and-future-roadmap.md).

## How to Use This Documentation During Delivery

1. Use this file as the orchestration document.
2. Use architecture documents to freeze implementation decisions before coding a slice.
3. Use backlog documents to choose the next small, independent work item.
4. When a decision changes, update the relevant architecture document first, then update the backlog if needed.
5. When coding begins, keep implementation aligned with the vocabulary and boundaries defined here.