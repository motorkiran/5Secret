using System.Text.Json;

namespace SecretManager.Shared.Contracts.Agents;

public static class AgentSnapshotIntegrity
{
	public static string ComputeHash(
		string snapshotId,
		Guid managedNodeId,
		Guid applicationId,
		Guid publishedVersionId,
		int versionNumber,
		string rolloutPolicy,
		DateTimeOffset updatedAtUtc,
		IReadOnlyList<AgentSnapshotValueResponse> values)
	{
		var hashMaterial = JsonSerializer.Serialize(new
		{
			SnapshotId = snapshotId,
			ManagedNodeId = managedNodeId,
			ApplicationId = applicationId,
			PublishedVersionId = publishedVersionId,
			VersionNumber = versionNumber,
			RolloutPolicy = rolloutPolicy,
			UpdatedAtUtc = updatedAtUtc,
			Values = values
		});

		return $"sha256:{ComputeSha256(hashMaterial)}";
	}

	public static bool Validate(AgentSnapshotResponse snapshot)
	{
		return string.Equals(
			snapshot.SnapshotHash,
			ComputeHash(
				snapshot.SnapshotId,
				snapshot.ManagedNodeId,
				snapshot.ApplicationId,
				snapshot.PublishedVersionId,
				snapshot.VersionNumber,
				snapshot.RolloutPolicy,
				snapshot.UpdatedAtUtc,
				snapshot.Values),
			StringComparison.Ordinal);
	}

	private static string ComputeSha256(string input)
	{
		return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
	}
}