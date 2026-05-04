namespace SecretManager.Domain.Catalog;

public sealed class PublishOperation
{
	public Guid Id { get; set; }

	public Guid EnvironmentId { get; set; }

	public Guid ApplicationId { get; set; }

	public Guid? InitiatedByUserId { get; set; }

	public string ChangeSummary { get; set; } = string.Empty;

	public string Status { get; set; } = string.Empty;

	public DateTimeOffset CreatedAtUtc { get; set; }

	public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class PublishedVersion
{
	public Guid Id { get; set; }

	public Guid PublishOperationId { get; set; }

	public Guid EnvironmentId { get; set; }

	public Guid ApplicationId { get; set; }

	public int VersionNumber { get; set; }

	public string RolloutPolicy { get; set; } = string.Empty;

	public string PayloadJson { get; set; } = string.Empty;

	public string ContentHash { get; set; } = string.Empty;

	public Guid? PublishedByUserId { get; set; }

	public DateTimeOffset PublishedAtUtc { get; set; }

	public Guid? SupersedesVersionId { get; set; }
}