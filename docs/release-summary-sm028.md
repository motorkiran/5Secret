# Release Summary: MVP Complete Through SM-028

## Commit

- Baseline delivery commit: `44f8848`
- Title: `feat: complete MVP through SM-028 and add operator guide`

## Scope

This delivery completes the documented MVP backlog through `SM-028`.

The completed scope includes:

- Control-plane bootstrap and local operator authentication.
- Environment, node group, and managed node topology management.
- Application, namespace, config item, and assignment catalog management.
- Draft editing, effective preview, publish, diff, and rollback workflows.
- Agent enrollment, sync, heartbeat, runtime delivery, and local encrypted state.
- Runtime and audit visibility in the operator UI.
- Local installation, startup, and operator documentation.

## Major Outcomes

- The Angular operator UI is complete for topology, catalog, workflow, and runtime surfaces.
- The control plane exposes the expected CRUD, publish, rollback, agent, and audit endpoints.
- A node-local worker can enroll, pull published configuration, serve runtime reads, and survive restart with persisted local state.
- The sample configuration provider reads values from the worker runtime API.
- The documented local happy path was validated end to end.

## Defects Fixed During Delivery

Two important runtime defects were found and fixed during the final hardening pass:

1. Installation-scoped imported drafts were excluded from effective preview and publish snapshot generation.
2. Draft secret decryption was not restart-safe until Data Protection keys were persisted.

## Validation Performed

- Focused backend tests passed for effective preview precedence and draft protection restart safety.
- Angular production build passed.
- Live end-to-end validation passed for:
  - bootstrap
  - topology and catalog setup
  - appsettings import
  - preview
  - publish
  - agent enrollment
  - agent sync and heartbeat
  - runtime reads through the sample app
  - update publish
  - rollback

## Operational Notes

- Keep the control-plane Data Protection key ring persisted.
- Keep the worker Data Protection key ring persisted.
- Keep the worker registration and encrypted snapshot state persisted.
- The Angular development proxy expects the local API on `http://localhost:5204` unless changed in `src/SecretManager.Web/proxy.conf.json`.

## Recommended Push

The repository remote is configured as:

- `origin https://github.com/motorkiran/5Secret.git`

To publish the current branch state:

```powershell
Set-Location "O:\DEV\Projects\SecretManager"
git push origin main
```

## Suggested PR Description

### Summary

Complete the documented MVP backlog through SM-028 and add the operator installation guide.

### What Changed

- implemented the control-plane, agent, sample provider, and Angular operator UI surfaces
- added publish, diff, rollback, agent enrollment, sync, runtime, and audit behavior
- added focused infrastructure and API tests for the delivered slices
- added local installation, operator usage, and happy-path packaging documentation
- fixed installation-scope preview and publish handling
- fixed restart-safe secret protection by persisting Data Protection key rings

### Validation

- `dotnet test` focused infrastructure tests passed
- Angular production build passed
- local end-to-end happy path validated against live API, PostgreSQL, Redis, worker, and sample runtime reads