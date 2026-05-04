using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.ConfigurationProvider;

public sealed class SecretManagerRuntimeConfigurationSource(
	SecretManagerRuntimeOptions options,
	SecretManagerRuntimeMetadataAccessor metadataAccessor) : IConfigurationSource
{
	public SecretManagerRuntimeOptions Options { get; } = options;

	public SecretManagerRuntimeMetadataAccessor MetadataAccessor { get; } = metadataAccessor;

	public IConfigurationProvider Build(IConfigurationBuilder builder)
	{
		return new SecretManagerRuntimeConfigurationProvider(Options, MetadataAccessor);
	}
}

public sealed class SecretManagerRuntimeConfigurationProvider(
	SecretManagerRuntimeOptions options,
	SecretManagerRuntimeMetadataAccessor metadataAccessor) : Microsoft.Extensions.Configuration.ConfigurationProvider, IDisposable
{
	private readonly SecretManagerRuntimeClient runtimeClient = new(options);
	private readonly object reloadLock = new();
	private Timer? refreshTimer;

	public override void Load()
	{
		var snapshot = runtimeClient.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
		ApplySnapshot(snapshot, triggerReload: false);

		if (options.RefreshInterval > TimeSpan.Zero && refreshTimer is null)
		{
			refreshTimer = new Timer(_ => Refresh(), null, options.RefreshInterval, options.RefreshInterval);
		}
	}

	public void Dispose()
	{
		refreshTimer?.Dispose();
		runtimeClient.Dispose();
	}

	private void Refresh()
	{
		try
		{
			var snapshot = runtimeClient.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
			if (string.Equals(metadataAccessor.Current?.SnapshotHash, snapshot.SnapshotHash, StringComparison.Ordinal))
			{
				return;
			}

			ApplySnapshot(snapshot, triggerReload: true);
		}
		catch
		{
		}
	}

	private void ApplySnapshot(RuntimeApplicationSnapshotResponse snapshot, bool triggerReload)
	{
		lock (reloadLock)
		{
			Data = Flatten(snapshot.Data);
			metadataAccessor.Update(new SecretManagerRuntimeMetadata(
				snapshot.ApplicationSlug,
				snapshot.PublishedVersionId,
				snapshot.VersionNumber,
				snapshot.SnapshotHash,
				snapshot.RolloutPolicy,
				snapshot.HealthState,
				snapshot.UpdatedAtUtc,
				snapshot.LastSuccessfulSyncAtUtc));

			if (triggerReload)
			{
				OnReload();
			}
		}
	}

	private static IDictionary<string, string?> Flatten(JsonElement element)
	{
		var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
		Visit(element, prefix: null, values);
		return values;
	}

	private static void Visit(JsonElement element, string? prefix, IDictionary<string, string?> values)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					Visit(property.Value, Combine(prefix, property.Name), values);
				}
				break;
			case JsonValueKind.Array:
				var index = 0;
				foreach (var item in element.EnumerateArray())
				{
					Visit(item, Combine(prefix, index.ToString()), values);
					index++;
				}
				break;
			case JsonValueKind.Null:
				if (prefix is not null)
				{
					values[prefix] = null;
				}
				break;
			default:
				if (prefix is not null)
				{
					values[prefix] = element.ValueKind == JsonValueKind.String
						? element.GetString()
						: element.GetRawText();
				}
				break;
		}
	}

	private static string Combine(string? prefix, string segment)
	{
		return string.IsNullOrWhiteSpace(prefix) ? segment : $"{prefix}:{segment}";
	}
}