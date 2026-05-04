using System.Text.Json;

namespace SecretManager.Shared.Contracts.Agents;

public sealed record RuntimeApplicationSnapshotResponse(
	string ApplicationSlug,
	Guid ApplicationId,
	Guid PublishedVersionId,
	int VersionNumber,
	string SnapshotHash,
	string RolloutPolicy,
	string HealthState,
	string ActivationState,
	DateTimeOffset UpdatedAtUtc,
	DateTimeOffset? LastSuccessfulSyncAtUtc,
	JsonElement Data);

public sealed record RuntimeApplicationVersionResponse(
	string ApplicationSlug,
	Guid ApplicationId,
	Guid PublishedVersionId,
	int VersionNumber,
	string SnapshotHash,
	string RolloutPolicy,
	string HealthState,
	string ActivationState,
	DateTimeOffset UpdatedAtUtc,
	DateTimeOffset? LastSuccessfulSyncAtUtc);

public sealed record RuntimeApplicationHealthResponse(
	string ApplicationSlug,
	string HealthState,
	string ActivationState,
	Guid? PublishedVersionId,
	int? VersionNumber,
	DateTimeOffset? LastSuccessfulSyncAtUtc,
	bool LocalStoreHealthy,
	string ProjectionState);

public sealed record RuntimeReloadResponse(
	string ApplicationSlug,
	Guid PublishedVersionId,
	int VersionNumber,
	string HealthState,
	string ActivationState);