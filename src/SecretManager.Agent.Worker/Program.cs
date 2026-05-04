using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecretManager.Agent.Worker.Configuration;
using SecretManager.Agent.Worker.ControlPlane;
using SecretManager.Agent.Worker.LocalState;
using SecretManager.Agent.Worker.Projection;
using SecretManager.Agent.Worker.Registration;
using SecretManager.Agent.Worker.Runtime;
using SecretManager.Infrastructure.Hosting;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Agent.Worker;

public static class AgentWorkerProgram
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);
		ConfigureServices(builder);

		var app = builder.Build();
		ConfigureApplication(app);
		app.Run();
	}

	public static void ConfigureServices(WebApplicationBuilder builder)
	{
		builder.AddSecretManagerServiceDefaults("secretmanager-agent-worker", isWeb: true);
		builder.Services.ConfigureHttpJsonOptions(options =>
		{
			options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
		});

		builder.Services
			.AddOptions<AgentOptions>()
			.Bind(builder.Configuration.GetSection(AgentOptions.SectionName));

		builder.Services
			.AddOptions<AgentLocalSnapshotStoreOptions>()
			.Configure<IConfiguration>((options, configuration) =>
			{
				options.FilePath = configuration["Agent:LocalSnapshotStore:FilePath"]
					?? Path.Combine(AppContext.BaseDirectory, "state", "snapshots.enc.json");
			});

		builder.Services
			.AddOptions<AgentRegistrationStateStoreOptions>()
			.Configure<IConfiguration>((options, configuration) =>
			{
				options.FilePath = configuration["Agent:RegistrationState:FilePath"]
					?? Path.Combine(AppContext.BaseDirectory, "state", "registration.protected.json");
			});

		builder.Services
			.AddOptions<AgentProjectionOptions>()
			.Bind(builder.Configuration.GetSection("Agent:Projection"));

		var keyRingPath = builder.Configuration["Agent:DataProtection:KeyRingPath"]
			?? Path.Combine(AppContext.BaseDirectory, "state", "keys");
		Directory.CreateDirectory(keyRingPath);
		builder.Services
			.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

		builder.Services.AddSingleton<IAgentRegistrationStateStore, AgentRegistrationStateStore>();
		builder.Services.AddSingleton<IAgentLocalSnapshotStore, AgentLocalSnapshotStore>();
		builder.Services.AddSingleton<IAgentProjectionService, AgentProjectionService>();
		builder.Services.AddSingleton<AgentRuntimeStateManager>();
		builder.Services.AddSingleton<IAgentCoordinator, AgentCoordinator>();
		builder.Services.AddSingleton<AgentSnapshotDocumentBuilder>();
		builder.Services.AddHttpClient<IControlPlaneAgentClient, HttpControlPlaneAgentClient>((services, client) =>
		{
			var agentOptions = services.GetRequiredService<IOptions<AgentOptions>>().Value;
			if (!string.IsNullOrWhiteSpace(agentOptions.ControlPlaneBaseUrl))
			{
				client.BaseAddress = new Uri(agentOptions.ControlPlaneBaseUrl, UriKind.Absolute);
			}
		});
		builder.Services.AddHostedService<Worker>();
	}

	public static void ConfigureApplication(WebApplication app)
	{
		app.MapGet(
			"/runtime/v1/applications/{applicationSlug}/snapshot",
			(string applicationSlug, IAgentCoordinator coordinator, AgentSnapshotDocumentBuilder documentBuilder) =>
			{
				if (!coordinator.TryGetApplication(applicationSlug, out var state) || state?.ActiveSnapshot is null)
				{
					return Results.NotFound();
				}

				var status = coordinator.GetStatus();
				return Results.Ok(new RuntimeApplicationSnapshotResponse(
					state.ApplicationSlug,
					state.ApplicationId,
					state.ActiveSnapshot.PublishedVersionId,
					state.ActiveSnapshot.VersionNumber,
					state.ActiveSnapshot.SnapshotHash,
					state.ActiveSnapshot.RolloutPolicy,
					status.HealthState,
					state.ActivationState,
					state.ActiveSnapshot.UpdatedAtUtc,
					status.LastSuccessfulSyncAtUtc,
					documentBuilder.Build(state.ActiveSnapshot)));
			});

		app.MapGet(
			"/runtime/v1/applications/{applicationSlug}/version",
			(string applicationSlug, IAgentCoordinator coordinator) =>
			{
				if (!coordinator.TryGetApplication(applicationSlug, out var state) || state?.ActiveSnapshot is null)
				{
					return Results.NotFound();
				}

				var status = coordinator.GetStatus();
				return Results.Ok(new RuntimeApplicationVersionResponse(
					state.ApplicationSlug,
					state.ApplicationId,
					state.ActiveSnapshot.PublishedVersionId,
					state.ActiveSnapshot.VersionNumber,
					state.ActiveSnapshot.SnapshotHash,
					state.ActiveSnapshot.RolloutPolicy,
					status.HealthState,
					state.ActivationState,
					state.ActiveSnapshot.UpdatedAtUtc,
					status.LastSuccessfulSyncAtUtc));
			});

		app.MapGet(
			"/runtime/v1/applications/{applicationSlug}/health",
			(string applicationSlug, IAgentCoordinator coordinator, IAgentProjectionService projectionService) =>
			{
				if (!coordinator.TryGetApplication(applicationSlug, out var state))
				{
					return Results.NotFound();
				}

				var status = coordinator.GetStatus();
				return Results.Ok(new RuntimeApplicationHealthResponse(
					state!.ApplicationSlug,
					status.HealthState,
					state.ActivationState,
					state.ActiveSnapshot?.PublishedVersionId,
					state.ActiveSnapshot?.VersionNumber,
					status.LastSuccessfulSyncAtUtc,
					status.LocalStoreHealthy,
					projectionService.GetProjectionState(applicationSlug)));
			});

		app.MapPost(
			"/runtime/v1/applications/{applicationSlug}/reload-ack",
			async (string applicationSlug, IAgentCoordinator coordinator, CancellationToken cancellationToken) =>
			{
				var promoted = await coordinator.PromoteStagedSnapshotAsync(applicationSlug, cancellationToken);
				if (!promoted || !coordinator.TryGetApplication(applicationSlug, out var state) || state?.ActiveSnapshot is null)
				{
					return Results.NotFound();
				}

				var status = coordinator.GetStatus();
				return Results.Ok(new RuntimeReloadResponse(
					state.ApplicationSlug,
					state.ActiveSnapshot.PublishedVersionId,
					state.ActiveSnapshot.VersionNumber,
					status.HealthState,
					state.ActivationState));
			});
	}
}
