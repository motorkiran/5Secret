using Microsoft.Extensions.Caching.Distributed;

namespace SecretManager.Infrastructure.Distribution;

public interface IAgentSnapshotCache
{
	Task SetAsync(string snapshotId, string payloadJson, CancellationToken cancellationToken);

	Task<string?> GetAsync(string snapshotId, CancellationToken cancellationToken);
}

public sealed class RedisAgentSnapshotCache(IDistributedCache distributedCache) : IAgentSnapshotCache
{
	public Task SetAsync(string snapshotId, string payloadJson, CancellationToken cancellationToken)
	{
		return distributedCache.SetStringAsync(
			snapshotId,
			payloadJson,
			new DistributedCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
			},
			cancellationToken);
	}

	public Task<string?> GetAsync(string snapshotId, CancellationToken cancellationToken)
	{
		return distributedCache.GetStringAsync(snapshotId, cancellationToken);
	}
}