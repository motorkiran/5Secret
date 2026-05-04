# Phase 2 and Future Roadmap

## Purpose

This document captures intentionally deferred capabilities so they do not get lost when the project pauses or when V1 delivery begins.

These items are not required for the first usable product, but they are important for long-term competitiveness and security maturity.

## Roadmap Principles

1. Do not add future features by weakening V1 boundaries.
2. Prefer features that extend existing models instead of bypassing them.
3. Keep deferred work visible and explicit.

## Phase 2 Candidates

### PM-001 High Availability Control Plane

Goal:

Enable active-passive and later active-active deployment options for the control plane.

Why it matters:

Some customers will require stronger availability guarantees than a single V1 control plane instance.

### PM-002 Service Accounts and Automation Tokens

Goal:

Allow CI/CD systems and operational automation to call the management API without human sessions.

Why it matters:

Enterprise customers often need controlled non-human access.

### PM-003 Approval Workflow and Four-Eyes Publish

Goal:

Allow one operator to prepare a change and another operator to approve or publish it.

Why it matters:

This becomes important in higher-risk production environments.

### PM-004 External Audit Export

Goal:

Export audit events to syslog, SIEM, webhook, or message bus destinations.

Why it matters:

Security teams often require centralized evidence pipelines.

### PM-005 SSO and LDAP Integration

Goal:

Support enterprise identity providers in addition to local accounts.

Why it matters:

This reduces operational overhead and aligns with corporate identity governance.

### PM-006 External KMS or HSM Integration

Goal:

Support external root key management beyond local runtime secrets.

Why it matters:

Some customers will require centralized key custody.

### PM-007 Secret Rotation Connectors

Goal:

Add optional rotation workflows for supported secret types such as database credentials or API keys.

Why it matters:

The product becomes more than a storage and delivery layer.

### PM-008 Advanced Rollout Orchestration

Goal:

Support staged rollout, canary rollout, percentage-based activation, and maintenance-window scheduling.

Why it matters:

Larger environments often need safer deployment control over configuration changes.

### PM-009 Drift Detection

Goal:

Detect whether local runtime state has diverged from the expected published snapshot.

Why it matters:

Operators need confidence that the intended state is really active.

### PM-010 Policy Templates and Presets

Goal:

Ship reusable RBAC and rollout templates for common operational models.

Why it matters:

This improves usability and reduces setup mistakes.

### PM-011 Multi-Factor Authentication

Goal:

Add MFA for sensitive roles and installation-wide enforcement options.

Why it matters:

This strengthens access security for a security-oriented product.

### PM-012 Recovery Tooling and Install Diagnostics

Goal:

Provide richer backup validation, restore tooling, and installation diagnostics.

Why it matters:

Self-hosted customers need stronger day-2 operations.

## Parking Lot

These ideas may become relevant later, but should not influence V1 design prematurely:

- Multi-installation federation.
- Hosted update channel.
- Compliance reporting packs.
- Source-control pull request workflow for config.

## Resume Checklist for Future Work

When V1 is complete and the project returns for expansion, use this order:

1. Re-evaluate customer feedback against this roadmap.
2. Convert the highest-value roadmap item into a detailed backlog document.
3. Confirm whether any V1 shortcut blocks the chosen Phase 2 item.
4. Update architecture docs before starting code.

## Priority Recommendation After V1

The recommended priority order after V1 is:

1. Service accounts and automation tokens.
2. MFA.
3. External audit export.
4. Approval workflow.
5. External KMS or HSM.
6. High availability control plane.
7. Advanced rollout orchestration.
8. Secret rotation connectors.