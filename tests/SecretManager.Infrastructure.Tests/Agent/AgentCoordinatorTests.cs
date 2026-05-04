using Microsoft.Extensions.Options;
using SecretManager.Agent.Worker.Configuration;
using SecretManager.Agent.Worker.ControlPlane;
using SecretManager.Agent.Worker.LocalState;
using SecretManager.Agent.Worker.Projection;
using SecretManager.Agent.Worker.Registration;
using SecretManager.Agent.Worker.Runtime;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Infrastructure.Tests.Agent;

public sealed class AgentCoordinatorTests : IDisposable
{
	private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-agent-coordinator-{Guid.NewGuid():N}");

	[Fact]
	public async Task InitializeAsync_RestoresLastValidSnapshot_AfterRestart_WhenSyncIsUnavailable()
	{
		Directory.CreateDirectory(tempDirectory);
		var registrationStore = new InMemoryRegistrationStateStore();
		var localSnapshotStore = new AgentLocalSnapshotStore(Options.Create(new AgentLocalSnapshotStoreOptions
		{
			FilePath = Path.Combine(tempDirectory, "snapshots.enc.json")
		}));
		var firstClient = new FakeControlPlaneAgentClient(CreateEnrollmentResponse(), CreateSyncCheckResponse(), CreateSnapshot());
		var options = Options.Create(new AgentOptions
		{
			ManagedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
			Hostname = "node01.local",
			Platform = "linux",
			AgentVersion = "1.2.3",
			EnrollmentToken = "bootstrap-enrollment-token"
		});

		var firstCoordinator = new AgentCoordinator(
			options,
			registrationStore,
			localSnapshotStore,
			new AgentRuntimeStateManager(),
			new NoOpProjectionService(),
			firstClient);
		await firstCoordinator.InitializeAsync(CancellationToken.None);

		Assert.Equal("healthy", firstCoordinator.GetStatus().HealthState);
		Assert.True(firstCoordinator.TryGetApplication("trading-api", out var initialState));
		Assert.NotNull(initialState?.ActiveSnapshot);

		var restartedCoordinator = new AgentCoordinator(
			options,
			registrationStore,
			localSnapshotStore,
			new AgentRuntimeStateManager(),
			new NoOpProjectionService(),
			new ThrowingControlPlaneAgentClient("control plane unavailable"));
		await restartedCoordinator.InitializeAsync(CancellationToken.None);

		var status = restartedCoordinator.GetStatus();
		Assert.Equal("degraded-offline", status.HealthState);
		Assert.True(status.IsEnrolled);
		Assert.True(restartedCoordinator.TryGetApplication("trading-api", out var restoredState));
		Assert.NotNull(restoredState?.ActiveSnapshot);
		Assert.Equal(1, restoredState!.ActiveSnapshot!.VersionNumber);
	}

	[Fact]
	public async Task InitializeAsync_DoesNotActivateCorruptedPersistedSnapshot()
	{
		Directory.CreateDirectory(tempDirectory);
		var registrationStore = new InMemoryRegistrationStateStore();
		var snapshotPath = Path.Combine(tempDirectory, "snapshots.enc.json");
		var localSnapshotStore = new AgentLocalSnapshotStore(Options.Create(new AgentLocalSnapshotStoreOptions
		{
			FilePath = snapshotPath
		}));
		var options = Options.Create(new AgentOptions
		{
			ManagedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
			Hostname = "node01.local",
			Platform = "linux",
			AgentVersion = "1.2.3",
			EnrollmentToken = "bootstrap-enrollment-token"
		});

		var seedCoordinator = new AgentCoordinator(
			options,
			registrationStore,
			localSnapshotStore,
			new AgentRuntimeStateManager(),
			new NoOpProjectionService(),
			new FakeControlPlaneAgentClient(CreateEnrollmentResponse(), CreateSyncCheckResponse(), CreateSnapshot()));
		await seedCoordinator.InitializeAsync(CancellationToken.None);
		Assert.Equal("healthy", seedCoordinator.GetStatus().HealthState);

		var payload = await File.ReadAllTextAsync(snapshotPath);
		await File.WriteAllTextAsync(snapshotPath, payload.Replace("a", "b", StringComparison.Ordinal));

		var restartedCoordinator = new AgentCoordinator(
			options,
			registrationStore,
			localSnapshotStore,
			new AgentRuntimeStateManager(),
			new NoOpProjectionService(),
			new ThrowingControlPlaneAgentClient("control plane unavailable"));
		await restartedCoordinator.InitializeAsync(CancellationToken.None);

		var status = restartedCoordinator.GetStatus();
		Assert.Equal("sync-failed", status.HealthState);
		Assert.False(status.LocalStoreHealthy);
		Assert.False(restartedCoordinator.TryGetApplication("trading-api", out _));
	}

	public void Dispose()
	{
		if (Directory.Exists(tempDirectory))
		{
			Directory.Delete(tempDirectory, recursive: true);
		}
	}

	private static AgentEnrollmentResponse CreateEnrollmentResponse()
	{
		return new AgentEnrollmentResponse(
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
			Guid.Parse("22222222-2222-2222-2222-222222222222"),
			"agent-credential-secret",
			"enrollment-secret-material",
			DateTimeOffset.Parse("2026-05-03T21:45:00Z"),
			[]);
	}

	private static AgentSyncCheckResponse CreateSyncCheckResponse()
	{
		var publishedVersionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
		var managedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		var applicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		var snapshotId = $"{publishedVersionId:N}.{managedNodeId:N}.{applicationId:N}";
		return new AgentSyncCheckResponse(
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			DateTimeOffset.Parse("2026-05-03T21:46:00Z"),
			[
				new AgentSyncSnapshotReferenceResponse(
					snapshotId,
					applicationId,
					"trading-api",
					publishedVersionId,
					1,
					CreateSnapshot().SnapshotHash,
					"immediate")
			]);
	}

	private static AgentSnapshotResponse CreateSnapshot()
	{
		var managedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		var applicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		var publishedVersionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
		var updatedAtUtc = DateTimeOffset.Parse("2026-05-03T21:46:00Z");
		var snapshotId = $"{publishedVersionId:N}.{managedNodeId:N}.{applicationId:N}";
		var values = new List<AgentSnapshotValueResponse>
		{
			new(
				Guid.Parse("44444444-4444-4444-4444-444444444444"),
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
			1,
			"immediate",
			updatedAtUtc,
			values);

		return new AgentSnapshotResponse(
			snapshotId,
			managedNodeId,
			applicationId,
			publishedVersionId,
			1,
			snapshotHash,
			"immediate",
			updatedAtUtc,
			"canonical",
			values);
	}

	private sealed class InMemoryRegistrationStateStore : IAgentRegistrationStateStore
	{
		private AgentRegistrationState? state;

		public Task<AgentRegistrationState?> LoadAsync(CancellationToken cancellationToken)
		{
			return Task.FromResult(state);
		}

		public Task SaveAsync(AgentRegistrationState state, CancellationToken cancellationToken)
		{
			this.state = state;
			return Task.CompletedTask;
		}
	}

	private sealed class NoOpProjectionService : IAgentProjectionService
	{
		public string GetProjectionState(string applicationSlug) => "not-configured";

		public void MarkPending(string applicationSlug)
		{
		}

		public Task ProjectAsync(AgentApplicationRuntimeState state, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class FakeControlPlaneAgentClient(
		AgentEnrollmentResponse enrollmentResponse,
		AgentSyncCheckResponse syncCheckResponse,
		AgentSnapshotResponse snapshotResponse) : IControlPlaneAgentClient
	{
		public Task<AgentEnrollmentResponse> EnrollAsync(AgentEnrollRequest request, CancellationToken cancellationToken)
		{
			return Task.FromResult(enrollmentResponse);
		}

		public Task<AgentSnapshotResponse> FetchSnapshotAsync(string snapshotId, Guid agentId, string agentCredential, CancellationToken cancellationToken)
		{
			return Task.FromResult(snapshotResponse);
		}

		public Task SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public async IAsyncEnumerable<AgentInvalidationNotification> SubscribeInvalidationsAsync(Guid agentId, string agentCredential, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.CompletedTask;
			yield break;
		}

		public Task<AgentSyncCheckResponse> SyncCheckAsync(Guid agentId, string agentCredential, CancellationToken cancellationToken)
		{
			return Task.FromResult(syncCheckResponse);
		}
	}

	private sealed class ThrowingControlPlaneAgentClient(string message) : IControlPlaneAgentClient
	{
		public Task<AgentEnrollmentResponse> EnrollAsync(AgentEnrollRequest request, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException(message);
		}

		public Task<AgentSnapshotResponse> FetchSnapshotAsync(string snapshotId, Guid agentId, string agentCredential, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException(message);
		}

		public Task SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException(message);
		}

		public IAsyncEnumerable<AgentInvalidationNotification> SubscribeInvalidationsAsync(Guid agentId, string agentCredential, CancellationToken cancellationToken)
		{
			return ThrowAsync(message);
		}

		private static async IAsyncEnumerable<AgentInvalidationNotification> ThrowAsync(string message)
		{
			await Task.FromException(new InvalidOperationException(message));
			yield break;
		}

		public Task<AgentSyncCheckResponse> SyncCheckAsync(Guid agentId, string agentCredential, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException(message);
		}
	}
}