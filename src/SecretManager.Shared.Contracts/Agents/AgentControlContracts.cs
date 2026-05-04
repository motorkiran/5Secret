using System.ComponentModel.DataAnnotations;

namespace SecretManager.Shared.Contracts.Agents;

public sealed record IssueAgentEnrollmentTokenResponse(
	Guid EnrollmentTokenId,
	Guid ManagedNodeId,
	DateTimeOffset ExpiresAtUtc,
	string EnrollmentToken);

public sealed class AgentEnrollRequest
{
	public Guid ManagedNodeId { get; set; }

	[Required]
	[MinLength(3)]
	[MaxLength(256)]
	public string Hostname { get; set; } = string.Empty;

	[MaxLength(64)]
	public string Platform { get; set; } = string.Empty;

	[MaxLength(64)]
	public string AgentVersion { get; set; } = string.Empty;

	[Required]
	[MinLength(16)]
	[MaxLength(512)]
	public string EnrollmentToken { get; set; } = string.Empty;
}

public sealed record AgentEnrollmentAssignmentResponse(
	Guid ApplicationId,
	Guid? PublishedVersionId,
	int? PublishedVersionNumber);

public sealed record AgentEnrollmentResponse(
	Guid AgentId,
	Guid ManagedNodeId,
	Guid EnvironmentId,
	string AgentCredential,
	string EnrollmentSecret,
	DateTimeOffset EnrolledAtUtc,
	IReadOnlyList<AgentEnrollmentAssignmentResponse> InitialAssignments);

public sealed class AgentHeartbeatRequest
{
	public Guid AgentId { get; set; }

	[Required]
	[MinLength(16)]
	[MaxLength(512)]
	public string AgentCredential { get; set; } = string.Empty;

	[MaxLength(64)]
	public string AgentVersion { get; set; } = string.Empty;

	public Guid? CurrentPublishedVersionId { get; set; }

	public int? CurrentVersionNumber { get; set; }
}

public sealed record AgentStatusResponse(
	Guid AgentId,
	Guid ManagedNodeId,
	Guid EnvironmentId,
	Guid? NodeGroupId,
	string Hostname,
	string AgentVersion,
	DateTimeOffset? LastSeenAtUtc,
	string HealthStatus,
	Guid? CurrentPublishedVersionId,
	int? CurrentVersionNumber);

public sealed class AgentSyncCheckRequest
{
	public Guid AgentId { get; set; }

	[Required]
	[MinLength(16)]
	[MaxLength(512)]
	public string AgentCredential { get; set; } = string.Empty;
}

public sealed record AgentSyncSnapshotReferenceResponse(
	string SnapshotId,
	Guid ApplicationId,
	string ApplicationSlug,
	Guid PublishedVersionId,
	int VersionNumber,
	string SnapshotHash,
	string RolloutPolicy);

public sealed record AgentSyncCheckResponse(
	Guid AgentId,
	DateTimeOffset GeneratedAtUtc,
	IReadOnlyList<AgentSyncSnapshotReferenceResponse> Snapshots);

public sealed record AgentSnapshotValueResponse(
	Guid ConfigItemId,
	string FullPath,
	string ValueType,
	string ValueJson,
	bool IsSecret,
	string SourceScopeType,
	Guid SourceScopeId);

public sealed record AgentSnapshotResponse(
	string SnapshotId,
	Guid ManagedNodeId,
	Guid ApplicationId,
	Guid PublishedVersionId,
	int VersionNumber,
	string SnapshotHash,
	string RolloutPolicy,
	DateTimeOffset UpdatedAtUtc,
	string Source,
	IReadOnlyList<AgentSnapshotValueResponse> Values);