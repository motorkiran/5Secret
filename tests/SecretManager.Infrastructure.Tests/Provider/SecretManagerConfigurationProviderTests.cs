using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using SecretManager.Agent.Worker;
using SecretManager.Agent.Worker.ControlPlane;
using SecretManager.Agent.Worker.Runtime;
using SecretManager.ConfigurationProvider;
using SecretManager.ConfigurationProvider.Sample;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Infrastructure.Tests.Provider;

public sealed class SecretManagerConfigurationProviderTests : IAsyncDisposable
{
	private readonly List<IAsyncDisposable> asyncDisposables = [];

	[Fact]
	public async Task ConfigurationProvider_LoadsRuntimeData_RefreshesOnTimer_AndExposesVersionMetadata()
	{
		var tempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-provider-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDirectory);
		var fakeClient = new MutableControlPlaneAgentClient(CreateEnrollmentResponse(), CreateSyncResponse(1, "immediate", CreateSnapshot(1, "3").SnapshotHash), CreateSnapshot(1, "3"));
		await using var app = await StartApplicationAsync(tempDirectory, fakeClient);
		var coordinator = app.Services.GetRequiredService<IAgentCoordinator>();
		await coordinator.InitializeAsync(CancellationToken.None);

		var configurationBuilder = new ConfigurationBuilder();
		configurationBuilder.AddSecretManagerRuntime(
			options =>
			{
				options.ApplicationSlug = "trading-api";
				options.HttpClient = app.GetTestClient();
				options.RefreshInterval = TimeSpan.FromMilliseconds(100);
			},
			out var metadataAccessor);
		var configuration = configurationBuilder.Build();
		var configurationRoot = Assert.IsType<ConfigurationRoot>(configuration);

		Assert.Equal("3", configuration["Trading:Core:MaxRetries"]);
		Assert.NotNull(metadataAccessor.Current);
		Assert.Equal(1, metadataAccessor.Current!.VersionNumber);
		Assert.Equal("healthy", metadataAccessor.Current.HealthState);

		var reloadTriggered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		ChangeToken.OnChange(configurationRoot.GetReloadToken, () => reloadTriggered.TrySetResult());

		fakeClient.SetSyncResponse(CreateSyncResponse(2, "immediate", CreateSnapshot(2, "5").SnapshotHash));
		fakeClient.SetSnapshot(CreateSnapshot(2, "5"));
		await coordinator.SyncNowAsync(CancellationToken.None);
		await reloadTriggered.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal("5", configuration["Trading:Core:MaxRetries"]);
		Assert.NotNull(metadataAccessor.Current);
		Assert.Equal(2, metadataAccessor.Current!.VersionNumber);
	}

	[Fact]
	public async Task SampleApplication_SmokeTest_ConsumesConfigurationThroughLocalAgent()
	{
		var tempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-provider-sample-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDirectory);
		var fakeClient = new MutableControlPlaneAgentClient(CreateEnrollmentResponse(), CreateSyncResponse(1, "immediate", CreateSnapshot(1, "7").SnapshotHash), CreateSnapshot(1, "7"));
		await using var app = await StartApplicationAsync(tempDirectory, fakeClient);
		var coordinator = app.Services.GetRequiredService<IAgentCoordinator>();
		await coordinator.InitializeAsync(CancellationToken.None);

		using var writer = new StringWriter();
		var exitCode = await SampleApplication.RunAsync(
			["--application-slug=trading-api", "--config-key=Trading:Core:MaxRetries"],
			app.GetTestClient(),
			writer,
			CancellationToken.None);

		Assert.Equal(0, exitCode);
		using var document = JsonDocument.Parse(writer.ToString());
		Assert.Equal("trading-api", document.RootElement.GetProperty("ApplicationSlug").GetString());
		Assert.Equal("Trading:Core:MaxRetries", document.RootElement.GetProperty("ConfigurationKey").GetString());
		Assert.Equal("7", document.RootElement.GetProperty("Value").GetString());
		Assert.Equal(1, document.RootElement.GetProperty("VersionNumber").GetInt32());
	}

	public async ValueTask DisposeAsync()
	{
		foreach (var disposable in asyncDisposables)
		{
			await disposable.DisposeAsync();
		}
	}

	private async Task<WebApplication> StartApplicationAsync(string tempDirectory, MutableControlPlaneAgentClient fakeClient)
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			EnvironmentName = "Test"
		});
		builder.WebHost.UseTestServer();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Infrastructure:Redis:Configuration"] = "localhost:6380,password=secretmanager,abortConnect=false",
			["Infrastructure:Redis:InstanceName"] = "secretmanager:",
			["Telemetry:EnableConsoleExporter"] = "false",
			["Agent:ManagedNodeId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
			["Agent:Hostname"] = "node01.local",
			["Agent:Platform"] = "linux",
			["Agent:AgentVersion"] = "1.2.3",
			["Agent:EnrollmentToken"] = "bootstrap-enrollment-token",
			["Agent:EnableBackgroundSync"] = "false",
			["Agent:EnableChangeNotifications"] = "false",
			["Agent:LocalSnapshotStore:FilePath"] = Path.Combine(tempDirectory, "snapshots.enc.json"),
			["Agent:RegistrationState:FilePath"] = Path.Combine(tempDirectory, "registration.protected.json"),
			["Agent:DataProtection:KeyRingPath"] = Path.Combine(tempDirectory, "keys")
		});

		AgentWorkerProgram.ConfigureServices(builder);
		builder.Services.RemoveAll(typeof(IControlPlaneAgentClient));
		builder.Services.AddSingleton<IControlPlaneAgentClient>(fakeClient);

		var app = builder.Build();
		AgentWorkerProgram.ConfigureApplication(app);
		await app.StartAsync();
		asyncDisposables.Add(app);
		return app;
	}

	private static AgentEnrollmentResponse CreateEnrollmentResponse()
	{
		return new AgentEnrollmentResponse(
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
			Guid.Parse("22222222-2222-2222-2222-222222222222"),
			"agent-credential-secret",
			"enrollment-secret-material",
			DateTimeOffset.Parse("2026-05-03T22:20:00Z"),
			[]);
	}

	private static AgentSyncCheckResponse CreateSyncResponse(int versionNumber, string rolloutPolicy, string snapshotHash)
	{
		var publishedVersionId = Guid.Parse($"00000000-0000-0000-0000-{versionNumber:000000000000}");
		var managedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		var applicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		var snapshotId = $"{publishedVersionId:N}.{managedNodeId:N}.{applicationId:N}";
		return new AgentSyncCheckResponse(
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			DateTimeOffset.Parse("2026-05-03T22:20:00Z"),
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

	private static AgentSnapshotResponse CreateSnapshot(int versionNumber, string valueJson)
	{
		var managedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		var applicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		var publishedVersionId = Guid.Parse($"00000000-0000-0000-0000-{versionNumber:000000000000}");
		var updatedAtUtc = DateTimeOffset.Parse("2026-05-03T22:20:00Z").AddMinutes(versionNumber);
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
			"immediate",
			updatedAtUtc,
			values);

		return new AgentSnapshotResponse(
			snapshotId,
			managedNodeId,
			applicationId,
			publishedVersionId,
			versionNumber,
			snapshotHash,
			"immediate",
			updatedAtUtc,
			"canonical",
			values);
	}

	private sealed class MutableControlPlaneAgentClient(
		AgentEnrollmentResponse enrollmentResponse,
		AgentSyncCheckResponse syncResponse,
		AgentSnapshotResponse snapshotResponse) : IControlPlaneAgentClient
	{
		private AgentSyncCheckResponse currentSyncResponse = syncResponse;
		private AgentSnapshotResponse currentSnapshotResponse = snapshotResponse;

		public Task<AgentEnrollmentResponse> EnrollAsync(AgentEnrollRequest request, CancellationToken cancellationToken)
			=> Task.FromResult(enrollmentResponse);

		public Task<AgentSnapshotResponse> FetchSnapshotAsync(string snapshotId, Guid agentId, string agentCredential, CancellationToken cancellationToken)
			=> Task.FromResult(currentSnapshotResponse);

		public Task SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken)
			=> Task.CompletedTask;

		public async IAsyncEnumerable<AgentInvalidationNotification> SubscribeInvalidationsAsync(Guid agentId, string agentCredential, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.CompletedTask;
			yield break;
		}

		public Task<AgentSyncCheckResponse> SyncCheckAsync(Guid agentId, string agentCredential, CancellationToken cancellationToken)
			=> Task.FromResult(currentSyncResponse);

		public void SetSyncResponse(AgentSyncCheckResponse response)
		{
			currentSyncResponse = response;
		}

		public void SetSnapshot(AgentSnapshotResponse response)
		{
			currentSnapshotResponse = response;
		}
	}
}