# Security and Cryptography

## Security Objectives

1. Protect secrets at rest, in transit, and during controlled reveal operations.
2. Ensure all writes are attributable to an authenticated user.
3. Ensure sensitive reads are auditable.
4. Minimize blast radius when one component is compromised.
5. Keep the design compatible with self-hosted Docker-based environments.

## Data Classification

SecretManager should classify stored items into at least three classes:

### Plain Configuration

Visible to authorized users without masking, but still versioned and auditable.

### Secret Configuration

Masked by default, encrypted at rest, reveal-controlled, and more heavily audited.

### Internal Security Metadata

User credentials, session data, agent enrollment metadata, and encryption metadata.

## Cryptographic Model

### Central Storage Encryption

Use envelope encryption.

1. Each secret value is encrypted with a data encryption key.
2. The data encryption key is wrapped by an installation root key.
3. The installation root key is never stored in PostgreSQL.
4. The installation root key is provided at runtime through a mounted secret or equivalent secure deployment mechanism.

### Recommended Algorithms

- AES-256-GCM for value encryption.
- HKDF or equivalent for key derivation where needed.
- SHA-256 or stronger for non-password integrity operations.
- Argon2id for password hashing.

If a specific library or platform constraint requires a different primitive, the choice must be documented before implementation.

## Root Key Handling

### V1 Recommendation

The installation root key should be supplied through Docker secret material, a mounted protected file, or an equivalent runtime secret source controlled by the customer.

### Why This Model

1. It works consistently across Windows, Linux, and edge devices.
2. It avoids coupling the design to OS-specific credential stores.
3. It keeps the database insufficient for decryption by itself.

### Rotation Requirement

The architecture must allow future rotation of the root key, even if the initial user experience for rotation is simple.

## Agent Local Snapshot Encryption

The agent must persist its last valid effective snapshot in encrypted form so the node can recover after restart or continue during control plane outages.

### V1 Recommendation

1. Each agent receives an enrollment secret during bootstrap.
2. The enrollment secret is stored as local runtime secret material, not in the database as plaintext.
3. The agent derives a local snapshot encryption key from the enrollment secret and local salt.
4. The encrypted snapshot is stored on a persistent local volume.

This provides an at-rest protection boundary for offline data while remaining practical for containerized deployment.

## Password and Session Security

### Password Storage

- Use Argon2id.
- Store per-user salt.
- Never store reversible passwords.

### Session Model

For the web UI and management API, V1 should prefer secure cookie-based sessions or short-lived access tokens paired with refresh logic owned by the control plane.

The exact implementation can be chosen during coding, but it must support:

- Session expiration.
- Explicit logout.
- Forced logout after password reset or role change.
- Audit correlation to user identity.

## Authorization Sensitivity Levels

Sensitive operations must be modeled separately, not as generic write permissions.

Examples:

- View masked value.
- Reveal plaintext secret.
- Edit draft.
- Delete draft override.
- Publish version.
- Roll back version.
- Manage users.
- Manage roles.
- View audit log.

## Agent Enrollment and Trust

### Enrollment Principles

1. A new agent must not self-authorize without a registration path.
2. Enrollment should bind the agent to a specific environment and managed node record.
3. The control plane must issue a durable agent identity after enrollment.
4. Enrollment events must be audited.

### Communication Pattern

Agents should establish outbound communication to the control plane. This works better in firewalled self-hosted installations and avoids requiring the control plane to reach every node directly.

## Secret Reveal Policy

Secret values should be masked by default in the UI and API.

When a user reveals a plaintext secret, the system should:

1. Check explicit reveal permission.
2. Audit the reveal event.
3. Return the plaintext only when required.
4. Avoid unnecessary plaintext logging or serialization.

## Audit Requirements

At minimum, audit the following:

- Login success and failure.
- Password changes.
- User and role changes.
- Create, update, delete of config data.
- Publish and rollback events.
- Secret reveal events.
- Agent enrollment and heartbeat anomalies.

V1 requires internal viewing only. Export to syslog or external SIEM is a later phase.

## Backup and Recovery Considerations

The product documentation and implementation must treat backup and recovery as part of the security design.

### Required Recovery Capabilities

1. Restore PostgreSQL without losing version integrity.
2. Restore Redis as a cache, not as the only durable copy.
3. Rehydrate agents from the control plane when online.
4. Allow agents to continue from last valid local snapshot when offline.

### Critical Warning

If the installation root key is lost, encrypted secret data may become unrecoverable. That operational risk must be documented clearly in the product setup instructions.