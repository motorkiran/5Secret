using System.Text.Json;
using Microsoft.Extensions.Options;
using SecretManager.Agent.Worker.LocalState;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Infrastructure.Tests.Agent;

public sealed class AgentLocalSnapshotStoreTests : IDisposable
{
	private const string EnrollmentSecret = "enrollment-secret-for-tests";
	private const string LocalSalt = "local-salt-for-tests";
	private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-agent-store-{Guid.NewGuid():N}");

	[Fact]
	public async Task SaveAsync_PersistsEncryptedPayload_AndLoadAsync_RestoresLastValidSnapshot()
	{
		var filePath = Path.Combine(tempDirectory, "snapshots.enc.json");
		var store = CreateStore(filePath);
		var snapshot = CreateSnapshot();
		var state = new AgentPersistedRuntimeStateDocument
		{
			Applications =
			[
				new AgentPersistedApplicationState
				{
					ApplicationId = snapshot.ApplicationId,
					ApplicationSlug = "trading-api",
					ActiveSnapshot = snapshot
				}
			]
		};

		await store.SaveAsync(state, EnrollmentSecret, LocalSalt, CancellationToken.None);

		var payload = await File.ReadAllTextAsync(filePath);
		Assert.DoesNotContain("Trading:Core:MaxRetries", payload, StringComparison.Ordinal);
		Assert.DoesNotContain(snapshot.SnapshotId, payload, StringComparison.Ordinal);

		var restored = await CreateStore(filePath).LoadAsync(EnrollmentSecret, LocalSalt, CancellationToken.None);

		Assert.True(restored.Success);
		Assert.NotNull(restored.State);
		var application = Assert.Single(restored.State!.Applications);
		Assert.Equal("trading-api", application.ApplicationSlug);
		Assert.NotNull(application.ActiveSnapshot);
		Assert.Equal(snapshot.SnapshotHash, application.ActiveSnapshot!.SnapshotHash);
		Assert.Equal(snapshot.Values[0].ValueJson, application.ActiveSnapshot.Values[0].ValueJson);
	}

	[Fact]
	public async Task LoadAsync_ReturnsFailure_WhenPersistedPayloadIsCorrupted()
	{
		var filePath = Path.Combine(tempDirectory, "snapshots.enc.json");
		var store = CreateStore(filePath);
		var snapshot = CreateSnapshot();
		var state = new AgentPersistedRuntimeStateDocument
		{
			Applications =
			[
				new AgentPersistedApplicationState
				{
					ApplicationId = snapshot.ApplicationId,
					ApplicationSlug = "trading-api",
					ActiveSnapshot = snapshot
				}
			]
		};

		await store.SaveAsync(state, EnrollmentSecret, LocalSalt, CancellationToken.None);

		var envelope = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await File.ReadAllTextAsync(filePath));
		Assert.NotNull(envelope);
		var corruptedCiphertext = Convert.FromBase64String(envelope!["ciphertext"].GetString()!);
		corruptedCiphertext[0] ^= 0xFF;
		envelope["ciphertext"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(corruptedCiphertext));
		await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(envelope));

		var restored = await CreateStore(filePath).LoadAsync(EnrollmentSecret, LocalSalt, CancellationToken.None);

		Assert.False(restored.Success);
		Assert.Null(restored.State);
		Assert.False(string.IsNullOrWhiteSpace(restored.FailureReason));
	}

	public void Dispose()
	{
		if (Directory.Exists(tempDirectory))
		{
			Directory.Delete(tempDirectory, recursive: true);
		}
	}

	private static AgentLocalSnapshotStore CreateStore(string filePath)
	{
		return new AgentLocalSnapshotStore(Options.Create(new AgentLocalSnapshotStoreOptions
		{
			FilePath = filePath
		}));
	}

	private static AgentSnapshotResponse CreateSnapshot()
	{
		var managedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		var applicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		var publishedVersionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
		var updatedAtUtc = DateTimeOffset.Parse("2026-05-03T20:15:00Z");
		var snapshotId = $"{publishedVersionId:N}.{managedNodeId:N}.{applicationId:N}";
		var values = new List<AgentSnapshotValueResponse>
		{
			new(
				Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
				"Trading:Core:MaxRetries",
				"integer",
				"3",
				false,
				"Application",
				applicationId)
		};
		var snapshotHash = AgentSnapshotIntegrity.ComputeHash(
			snapshotId,
			managedNodeId,
			applicationId,
			publishedVersionId,
			7,
			"immediate",
			updatedAtUtc,
			values);

		return new AgentSnapshotResponse(
			snapshotId,
			managedNodeId,
			applicationId,
			publishedVersionId,
			7,
			snapshotHash,
			"immediate",
			updatedAtUtc,
			"canonical",
			values);
	}
}