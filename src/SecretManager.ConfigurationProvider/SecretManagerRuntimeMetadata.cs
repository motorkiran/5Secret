namespace SecretManager.ConfigurationProvider;

public sealed record SecretManagerRuntimeMetadata(
	string ApplicationSlug,
	Guid PublishedVersionId,
	int VersionNumber,
	string SnapshotHash,
	string RolloutPolicy,
	string HealthState,
	DateTimeOffset UpdatedAtUtc,
	DateTimeOffset? LastSuccessfulSyncAtUtc);

public sealed class SecretManagerRuntimeMetadataAccessor
{
	private readonly Lock gate = new();
	private SecretManagerRuntimeMetadata? current;

	public SecretManagerRuntimeMetadata? Current
	{
		get
		{
			lock (gate)
			{
				return current;
			}
		}
	}

	internal void Update(SecretManagerRuntimeMetadata metadata)
	{
		lock (gate)
		{
			current = metadata;
		}
	}
}