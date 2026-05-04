namespace SecretManager.Shared.Contracts.Agents;

public sealed record AgentInvalidationNotification(
	Guid AgentId,
	Guid ApplicationId,
	Guid PublishedVersionId,
	int VersionNumber,
	DateTimeOffset PublishedAtUtc);