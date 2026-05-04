namespace SecretManager.Domain.Agents;

public sealed class AgentEnrollmentToken
{
	public Guid Id { get; set; }

	public Guid ManagedNodeId { get; set; }

	public string TokenHash { get; set; } = string.Empty;

	public Guid? IssuedByUserId { get; set; }

	public DateTimeOffset CreatedAtUtc { get; set; }

	public DateTimeOffset ExpiresAtUtc { get; set; }

	public DateTimeOffset? ConsumedAtUtc { get; set; }

	public Guid? ConsumedByAgentId { get; set; }
}

public sealed class AgentRegistration
{
	public Guid Id { get; set; }

	public Guid ManagedNodeId { get; set; }

	public string CredentialHash { get; set; } = string.Empty;

	public DateTimeOffset? LastSeenAtUtc { get; set; }

	public Guid? CurrentPublishedVersionId { get; set; }

	public int? CurrentVersionNumber { get; set; }

	public string HealthStatus { get; set; } = string.Empty;

	public DateTimeOffset EnrolledAtUtc { get; set; }

	public DateTimeOffset CreatedAtUtc { get; set; }

	public DateTimeOffset UpdatedAtUtc { get; set; }
}