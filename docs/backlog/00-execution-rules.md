# Backlog Execution Rules

## Purpose

This document explains how to execute the implementation backlog so that the project remains understandable after pauses, handoffs, or model changes.

## Core Rule

Every backlog item must be small enough to complete as one isolated unit of value and one meaningful commit.

## Required Shape of a Work Item

Every work item should include:

1. Goal.
2. Scope.
3. Explicit out-of-scope note.
4. Dependencies.
5. Acceptance criteria.
6. Verification approach.

## Commit Guidance

One work item does not have to mean one file. It means one coherent behavioral change that can be reviewed and reverted independently.

Examples of good work item size:

- Add bootstrap owner creation flow.
- Add node group CRUD API and persistence.
- Add published snapshot resolver.

Examples of bad work item size:

- Build the full backend.
- Build all agent features.
- Build the whole UI.

## Recommended Delivery Order

1. Freeze vocabulary and architecture.
2. Build installation bootstrap and security foundations.
3. Build topology and metadata management.
4. Build draft editing and publish pipeline.
5. Build agent enrollment and sync.
6. Build application integration.
7. Build UI slices around already-working backend behavior.
8. Harden and document operations.

## Definition of Done for a Work Item

A work item is done only when:

1. The documented scope is implemented.
2. Acceptance criteria pass.
3. Relevant tests or validation checks exist.
4. Documentation changes are updated if the work changed design assumptions.
5. The item can be explained independently in a commit message.

## Change Control Rule

If coding reveals that an architecture decision is wrong or incomplete, update the architecture document first or in the same change set. Do not let the code become the only source of truth.

## Re-entry Rule After a Pause

When returning to the project after weeks or months:

1. Read [../README.md](../README.md).
2. Read the current architecture document that matches the next work item.
3. Read [01-mvp-work-items.md](01-mvp-work-items.md).
4. Resume from the first incomplete item.

## Branch and Commit Suggestion

Recommended naming pattern:

- Branch: `feature/sm-###-short-name`
- Commit: `SM-### short summary`

This keeps the eventual delivery history aligned with the backlog.