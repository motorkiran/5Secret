using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecretManager.Agent.Worker;
using SecretManager.Agent.Worker.ControlPlane;
using SecretManager.Agent.Worker.Runtime;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Infrastructure.Tests.Agent;

public sealed class AgentRuntimeApiTests : IAsyncDisposable
{
	private readonly List<IAsyncDisposable> asyncDisposables = [];

	[Fact]
	public async Task RuntimeEndpoints_ServeActiveSnapshot_AndPromoteManualReload_OnReloadAck()
	{
		var tempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-agent-runtime-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDirectory);
		var fakeClient = new MutableControlPlaneAgentClient(CreateEnrollmentResponse(), CreateImmediateSyncResponse(), CreateSnapshot(versionNumber: 1, rolloutPolicy: "immediate", valueJson: "3"));
		await using var app = await StartApplicationAsync(tempDirectory, fakeClient, enableBackgroundSync: false, syncPollIntervalSeconds: 30, enableChangeNotifications: false);
		var coordinator = app.Services.GetRequiredService<IAgentCoordinator>();
		await coordinator.InitializeAsync(CancellationToken.None);

		var client = app.GetTestClient();
		var snapshot = await client.GetFromJsonAsync<RuntimeApplicationSnapshotResponse>("/runtime/v1/applications/trading-api/snapshot");
		Assert.NotNull(snapshot);
		Assert.Equal(1, snapshot!.VersionNumber);
		Assert.Equal(3, snapshot.Data.GetProperty("Trading").GetProperty("Core").GetProperty("MaxRetries").GetInt32());

		var health = await client.GetFromJsonAsync<RuntimeApplicationHealthResponse>("/runtime/v1/applications/trading-api/health");
		Assert.NotNull(health);
		Assert.Equal("healthy", health!.HealthState);
		Assert.Equal("active", health.ActivationState);
		Assert.Equal("not-configured", health.ProjectionState);

		fakeClient.SetSyncResponse(CreateManualReloadSyncResponse());
		fakeClient.SetSnapshot(CreateSnapshot(versionNumber: 2, rolloutPolicy: "manual-reload", valueJson: "5"));
		await coordinator.SyncNowAsync(CancellationToken.None);

		var stagedVersion = await client.GetFromJsonAsync<RuntimeApplicationVersionResponse>("/runtime/v1/applications/trading-api/version");
		Assert.NotNull(stagedVersion);
		Assert.Equal(1, stagedVersion!.VersionNumber);

		var stagedHealth = await client.GetFromJsonAsync<RuntimeApplicationHealthResponse>("/runtime/v1/applications/trading-api/health");
		Assert.NotNull(stagedHealth);
		Assert.Equal("staged", stagedHealth!.ActivationState);

		var reloadResponse = await client.PostAsync("/runtime/v1/applications/trading-api/reload-ack", content: null);
		Assert.Equal(HttpStatusCode.OK, reloadResponse.StatusCode);

		var reloadedVersion = await client.GetFromJsonAsync<RuntimeApplicationVersionResponse>("/runtime/v1/applications/trading-api/version");
		Assert.NotNull(reloadedVersion);
		Assert.Equal(2, reloadedVersion!.VersionNumber);

		var reloadedSnapshot = await client.GetFromJsonAsync<RuntimeApplicationSnapshotResponse>("/runtime/v1/applications/trading-api/snapshot");
		Assert.NotNull(reloadedSnapshot);
		Assert.Equal(5, reloadedSnapshot!.Data.GetProperty("Trading").GetProperty("Core").GetProperty("MaxRetries").GetInt32());
	}

	[Fact]
	public async Task BackgroundWorker_UsesInvalidationStream_First_AndPollingFallback_WhenNotificationsFail()
	{
		var notificationTempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-agent-runtime-notify-{Guid.NewGuid():N}");
		Directory.CreateDirectory(notificationTempDirectory);
		var notificationClient = new MutableControlPlaneAgentClient(CreateEnrollmentResponse(), CreateImmediateSyncResponse(), CreateSnapshot(versionNumber: 1, rolloutPolicy: "immediate", valueJson: "3"));
		await using var notificationApp = await StartApplicationAsync(notificationTempDirectory, notificationClient, enableBackgroundSync: true, syncPollIntervalSeconds: 60, enableChangeNotifications: true);
		var notificationHttpClient = notificationApp.GetTestClient();
		await WaitForVersionAsync(notificationHttpClient, 1, TimeSpan.FromSeconds(3));

		notificationClient.SetSyncResponse(CreateSyncResponse(2, "immediate", CreateSnapshot(versionNumber: 2, rolloutPolicy: "immediate", valueJson: "5").SnapshotHash));
		notificationClient.SetSnapshot(CreateSnapshot(versionNumber: 2, rolloutPolicy: "immediate", valueJson: "5"));
		notificationClient.PublishInvalidation(new AgentInvalidationNotification(
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
			Guid.Parse("00000000-0000-0000-0000-000000000002"),
			2,
			DateTimeOffset.UtcNow));
		await WaitForVersionAsync(notificationHttpClient, 2, TimeSpan.FromSeconds(3));

		var pollingTempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-agent-runtime-poll-{Guid.NewGuid():N}");
		Directory.CreateDirectory(pollingTempDirectory);
		var pollingClient = new MutableControlPlaneAgentClient(CreateEnrollmentResponse(), CreateImmediateSyncResponse(), CreateSnapshot(versionNumber: 1, rolloutPolicy: "immediate", valueJson: "3"))
		{
			ThrowOnNotifications = true
		};
		await using var pollingApp = await StartApplicationAsync(pollingTempDirectory, pollingClient, enableBackgroundSync: true, syncPollIntervalSeconds: 1, enableChangeNotifications: true);
		var pollingHttpClient = pollingApp.GetTestClient();
		await WaitForVersionAsync(pollingHttpClient, 1, TimeSpan.FromSeconds(3));

		pollingClient.SetSyncResponse(CreateSyncResponse(2, "immediate", CreateSnapshot(versionNumber: 2, rolloutPolicy: "immediate", valueJson: "7").SnapshotHash));
		pollingClient.SetSnapshot(CreateSnapshot(versionNumber: 2, rolloutPolicy: "immediate", valueJson: "7"));
		await WaitForVersionAsync(pollingHttpClient, 2, TimeSpan.FromSeconds(5));
	}

	[Fact]
	public async Task ProjectionMode_WritesActiveJson_AndWaitsForActivation_BeforeUpdatingFile()
	{
		var tempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-agent-runtime-projection-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDirectory);
		var projectionFilePath = Path.Combine(tempDirectory, "trading-api.settings.json");
		var fakeClient = new MutableControlPlaneAgentClient(CreateEnrollmentResponse(), CreateImmediateSyncResponse(), CreateSnapshot(versionNumber: 1, rolloutPolicy: "immediate", valueJson: "3"));
		await using var app = await StartApplicationAsync(tempDirectory, fakeClient, enableBackgroundSync: false, syncPollIntervalSeconds: 30, enableChangeNotifications: false, projectionFilePath: projectionFilePath);
		var coordinator = app.Services.GetRequiredService<IAgentCoordinator>();
		await coordinator.InitializeAsync(CancellationToken.None);

		Assert.True(File.Exists(projectionFilePath));
		Assert.Contains("\"MaxRetries\": 3", await File.ReadAllTextAsync(projectionFilePath), StringComparison.Ordinal);

		fakeClient.SetSyncResponse(CreateManualReloadSyncResponse());
		fakeClient.SetSnapshot(CreateSnapshot(versionNumber: 2, rolloutPolicy: "manual-reload", valueJson: "5"));
		await coordinator.SyncNowAsync(CancellationToken.None);

		Assert.Contains("\"MaxRetries\": 3", await File.ReadAllTextAsync(projectionFilePath), StringComparison.Ordinal);

		var client = app.GetTestClient();
		var health = await client.GetFromJsonAsync<RuntimeApplicationHealthResponse>("/runtime/v1/applications/trading-api/health");
		Assert.NotNull(health);
		Assert.Equal("pending", health!.ProjectionState);

		var reloadResponse = await client.PostAsync("/runtime/v1/applications/trading-api/reload-ack", content: null);
		Assert.Equal(HttpStatusCode.OK, reloadResponse.StatusCode);
		Assert.Contains("\"MaxRetries\": 5", await File.ReadAllTextAsync(projectionFilePath), StringComparison.Ordinal);

		var reloadedHealth = await client.GetFromJsonAsync<RuntimeApplicationHealthResponse>("/runtime/v1/applications/trading-api/health");
		Assert.NotNull(reloadedHealth);
		Assert.Equal("current", reloadedHealth!.ProjectionState);
	}

	public async ValueTask DisposeAsync()
	{
		foreach (var disposable in asyncDisposables)
		{
			await disposable.DisposeAsync();
		}
	}

	private async Task<WebApplication> StartApplicationAsync(
		string tempDirectory,
		MutableControlPlaneAgentClient fakeClient,
		bool enableBackgroundSync,
		int syncPollIntervalSeconds,
		bool enableChangeNotifications,
		string? projectionFilePath = null)
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			EnvironmentName = "Test"
		});
		builder.WebHost.UseTestServer();
		var configuration = new Dictionary<string, string?>
		{
			["Infrastructure:Redis:Configuration"] = "localhost:6380,password=secretmanager,abortConnect=false",
			["Infrastructure:Redis:InstanceName"] = "secretmanager:",
			["Telemetry:EnableConsoleExporter"] = "false",
			["Agent:ManagedNodeId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
			["Agent:Hostname"] = "node01.local",
			["Agent:Platform"] = "linux",
			["Agent:AgentVersion"] = "1.2.3",
			["Agent:EnrollmentToken"] = "bootstrap-enrollment-token",
			["Agent:EnableBackgroundSync"] = enableBackgroundSync.ToString(),
			["Agent:SyncPollIntervalSeconds"] = syncPollIntervalSeconds.ToString(),
			["Agent:EnableChangeNotifications"] = enableChangeNotifications.ToString(),
			["Agent:NotificationReconnectDelaySeconds"] = "1",
			["Agent:LocalSnapshotStore:FilePath"] = Path.Combine(tempDirectory, "snapshots.enc.json"),
			["Agent:RegistrationState:FilePath"] = Path.Combine(tempDirectory, "registration.protected.json"),
			["Agent:DataProtection:KeyRingPath"] = Path.Combine(tempDirectory, "keys")
		};
		if (!string.IsNullOrWhiteSpace(projectionFilePath))
		{
			configuration["Agent:Projection:Targets:0:ApplicationSlug"] = "trading-api";
			configuration["Agent:Projection:Targets:0:FilePath"] = projectionFilePath;
		}
		builder.Configuration.AddInMemoryCollection(configuration);

		AgentWorkerProgram.ConfigureServices(builder);
		builder.Services.RemoveAll(typeof(IControlPlaneAgentClient));
		builder.Services.AddSingleton<IControlPlaneAgentClient>(fakeClient);

		var app = builder.Build();
		AgentWorkerProgram.ConfigureApplication(app);
		await app.StartAsync();
		asyncDisposables.Add(app);
		return app;
	}

	private static async Task WaitForVersionAsync(HttpClient client, int expectedVersion, TimeSpan timeout)
	{
		using var cts = new CancellationTokenSource(timeout);
		while (!cts.IsCancellationRequested)
		{
			var response = await client.GetAsync("/runtime/v1/applications/trading-api/version", cts.Token);
			if (response.StatusCode == HttpStatusCode.OK)
			{
				var version = await response.Content.ReadFromJsonAsync<RuntimeApplicationVersionResponse>(cancellationToken: cts.Token);
				if (version?.VersionNumber == expectedVersion)
				{
					return;
				}
			}

			await Task.Delay(100, cts.Token);
		}

		throw new TimeoutException($"Timed out waiting for runtime version {expectedVersion}.");
	}

	private static AgentEnrollmentResponse CreateEnrollmentResponse()
	{
		return new AgentEnrollmentResponse(
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
			Guid.Parse("22222222-2222-2222-2222-222222222222"),
			"agent-credential-secret",
			"enrollment-secret-material",
			DateTimeOffset.Parse("2026-05-03T22:00:00Z"),
			[]);
	}

	private static AgentSyncCheckResponse CreateImmediateSyncResponse()
		=> CreateSyncResponse(1, "immediate", CreateSnapshot(versionNumber: 1, rolloutPolicy: "immediate", valueJson: "3").SnapshotHash);

	private static AgentSyncCheckResponse CreateManualReloadSyncResponse()
		=> CreateSyncResponse(2, "manual-reload", CreateSnapshot(versionNumber: 2, rolloutPolicy: "manual-reload", valueJson: "5").SnapshotHash);

	private static AgentSyncCheckResponse CreateSyncResponse(int versionNumber, string rolloutPolicy, string snapshotHash)
	{
		var publishedVersionId = Guid.Parse($"00000000-0000-0000-0000-{versionNumber:000000000000}");
		var managedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		var applicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		var snapshotId = $"{publishedVersionId:N}.{managedNodeId:N}.{applicationId:N}";
		return new AgentSyncCheckResponse(
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			DateTimeOffset.Parse("2026-05-03T22:00:00Z"),
			[
				new AgentSyncSnapshotReferenceResponse(
					snapshotId,
					applicationId,
					"trading-api",
					publishedVersionId,
					versionNumber,
					snapshotHash,
					rolloutPolicy)
			]);
	}

	private static AgentSnapshotResponse CreateSnapshot(int versionNumber, string rolloutPolicy, string valueJson)
	{
		var managedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		var applicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		var publishedVersionId = Guid.Parse($"00000000-0000-0000-0000-{versionNumber:000000000000}");
		var updatedAtUtc = DateTimeOffset.Parse("2026-05-03T22:00:00Z").AddMinutes(versionNumber);
		var snapshotId = $"{publishedVersionId:N}.{managedNodeId:N}.{applicationId:N}";
		var values = new List<AgentSnapshotValueResponse>
		{
			new(
				Guid.Parse("44444444-4444-4444-4444-444444444444"),
				"Trading:Core:MaxRetries",
				"integer",
				valueJson,
				false,
				"Application",
				applicationId)
		};
		var snapshotHash = AgentSnapshotIntegrity.ComputeHash(
			snapshotId,
			managedNodeId,
			applicationId,
			publishedVersionId,
			versionNumber,
			rolloutPolicy,
			updatedAtUtc,
			values);

		return new AgentSnapshotResponse(
			snapshotId,
			managedNodeId,
			applicationId,
			publishedVersionId,
			versionNumber,
			snapshotHash,
			rolloutPolicy,
			updatedAtUtc,
			"canonical",
			values);
	}

	private sealed class MutableControlPlaneAgentClient(
		AgentEnrollmentResponse enrollmentResponse,
		AgentSyncCheckResponse syncResponse,
		AgentSnapshotResponse snapshotResponse) : IControlPlaneAgentClient
	{
		private readonly Channel<AgentInvalidationNotification> notifications = Channel.CreateUnbounded<AgentInvalidationNotification>();
		private AgentSyncCheckResponse currentSyncResponse = syncResponse;
		private AgentSnapshotResponse currentSnapshotResponse = snapshotResponse;
		public bool ThrowOnNotifications { get; set; }

		public Task<AgentEnrollmentResponse> EnrollAsync(AgentEnrollRequest request, CancellationToken cancellationToken)
			=> Task.FromResult(enrollmentResponse);

		public Task<AgentSnapshotResponse> FetchSnapshotAsync(string snapshotId, Guid agentId, string agentCredential, CancellationToken cancellationToken)
			=> Task.FromResult(currentSnapshotResponse);

		public Task SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken)
			=> Task.CompletedTask;

		public Task<AgentSyncCheckResponse> SyncCheckAsync(Guid agentId, string agentCredential, CancellationToken cancellationToken)
			=> Task.FromResult(currentSyncResponse);

		public async IAsyncEnumerable<AgentInvalidationNotification> SubscribeInvalidationsAsync(Guid agentId, string agentCredential, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			if (ThrowOnNotifications)
			{
				throw new InvalidOperationException("notifications unavailable");
			}

			while (await notifications.Reader.WaitToReadAsync(cancellationToken))
			{
				while (notifications.Reader.TryRead(out var notification))
				{
					yield return notification;
				}
			}
		}

		public void SetSyncResponse(AgentSyncCheckResponse response)
		{
			currentSyncResponse = response;
		}

		public void SetSnapshot(AgentSnapshotResponse response)
		{
			currentSnapshotResponse = response;
		}

		public void PublishInvalidation(AgentInvalidationNotification notification)
		{
			notifications.Writer.TryWrite(notification);
		}
	}
}