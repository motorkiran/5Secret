using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecretManager.ControlPlane.Api.AgentNotifications;
using SecretManager.ControlPlane.Application.Authorization;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Catalog;
using SecretManager.Domain.Environments;
using SecretManager.Domain.Installations;
using SecretManager.Domain.Topology;
using SecretManager.Domain.Users;
using SecretManager.Infrastructure.Distribution;
using SecretManager.Infrastructure.Persistence;
using SecretManager.Infrastructure.Security;
using SecretManager.Shared.Contracts.Agents;
using SecretManager.Shared.Contracts.Auth;
using SecretManager.Shared.Contracts.Catalog;

namespace SecretManager.Infrastructure.Tests.Api;

public sealed class AgentSynchronizationEndpointsTests
{
	[Theory]
	[InlineData(true, "redis")]
	[InlineData(false, "canonical")]
	public async Task AgentSnapshotFetch_UsesRedisCache_WhenAvailable_AndFallsBackOtherwise(bool cacheAvailable, string expectedSource)
	{
		var snapshotCache = new TestAgentSnapshotCache(cacheAvailable);
		using var factory = new AgentSynchronizationApiFactory(snapshotCache);
		var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
		var nodeGroupId = await factory.SeedNodeGroupAsync(environmentId, "Backend", "backend");
		var managedNodeId = await factory.SeedManagedNodeAsync(environmentId, nodeGroupId, "Node 01", "node01.local");
		var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
		var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
		var configItemId = await factory.SeedConfigItemAsync(
			applicationId,
			namespaceId,
			"MaxRetries",
			"Trading:Core:MaxRetries",
			isSecret: false,
			valueType: "integer");

		await factory.SeedApplicationAssignmentAsync(applicationId, environmentId, nodeGroupId: nodeGroupId);
		await factory.SeedDraftValueAsync(configItemId, ResourceScopeType.Application, applicationId, "3", isSecret: false);

		using var operatorClient = await factory.CreateAuthenticatedClientAsync(
			PermissionCatalog.AgentsWrite,
			PermissionCatalog.ConfigPublish);

		var publishResponse = await operatorClient.PostAsJsonAsync(
			"/api/v1/publishes",
			new CreatePublishRequest
			{
				ApplicationId = applicationId,
				EnvironmentId = environmentId,
				ChangeSummary = "Initial publish",
				RolloutPolicy = "immediate"
			});
		var publish = await publishResponse.Content.ReadFromJsonAsync<CreatePublishResponse>();
		Assert.NotNull(publish);

		var tokenResponse = await operatorClient.PostAsync($"/api/v1/nodes/{managedNodeId}/enrollment-token", content: null);
		var token = await tokenResponse.Content.ReadFromJsonAsync<IssueAgentEnrollmentTokenResponse>();
		Assert.NotNull(token);

		using var agentClient = factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			HandleCookies = false
		});

		var enrollResponse = await agentClient.PostAsJsonAsync(
			"/api/v1/agent/enroll",
			new AgentEnrollRequest
			{
				ManagedNodeId = managedNodeId,
				Hostname = "node01.local",
				Platform = "linux",
				AgentVersion = "1.2.3",
				EnrollmentToken = token!.EnrollmentToken
			});
		var enrollment = await enrollResponse.Content.ReadFromJsonAsync<AgentEnrollmentResponse>();
		Assert.NotNull(enrollment);

		var syncCheckResponse = await agentClient.PostAsJsonAsync(
			"/api/v1/agent/sync/check",
			new AgentSyncCheckRequest
			{
				AgentId = enrollment!.AgentId,
				AgentCredential = enrollment.AgentCredential
			});
		var syncCheckBody = await syncCheckResponse.Content.ReadAsStringAsync();
		Assert.True(syncCheckResponse.StatusCode == HttpStatusCode.OK, syncCheckBody);
		var syncCheck = await syncCheckResponse.Content.ReadFromJsonAsync<AgentSyncCheckResponse>();
		Assert.NotNull(syncCheck);
		var snapshotRef = Assert.Single(syncCheck!.Snapshots);
		Assert.Equal("trading-api", snapshotRef.ApplicationSlug);

		var snapshotResponse = await agentClient.GetAsync(
			$"/api/v1/agent/snapshots/{snapshotRef.SnapshotId}?agentId={enrollment.AgentId}&agentCredential={enrollment.AgentCredential}");
		var snapshotBody = await snapshotResponse.Content.ReadAsStringAsync();
		Assert.True(snapshotResponse.StatusCode == HttpStatusCode.OK, snapshotBody);
		var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<AgentSnapshotResponse>();
		Assert.NotNull(snapshot);
		Assert.Equal(expectedSource, snapshot!.Source);
		Assert.Equal(snapshotRef.SnapshotHash, snapshot.SnapshotHash);
		Assert.Equal(publish!.PublishedVersion.PublishedVersionId, snapshot.PublishedVersionId);
		var value = Assert.Single(snapshot.Values);
		Assert.Equal("Trading:Core:MaxRetries", value.FullPath);
		Assert.Equal("3", value.ValueJson);
	}

	[Fact]
	public async Task Publish_RaisesInvalidationNotification_ForAssignedAgent()
	{
		using var factory = new AgentSynchronizationApiFactory(new TestAgentSnapshotCache(true));
		var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
		var nodeGroupId = await factory.SeedNodeGroupAsync(environmentId, "Backend", "backend");
		var managedNodeId = await factory.SeedManagedNodeAsync(environmentId, nodeGroupId, "Node 01", "node01.local");
		var applicationId = await factory.SeedApplicationAsync("Trading API", "trading-api");
		var namespaceId = await factory.SeedNamespaceAsync(applicationId, "Core", "Trading:Core");
		var configItemId = await factory.SeedConfigItemAsync(
			applicationId,
			namespaceId,
			"MaxRetries",
			"Trading:Core:MaxRetries",
			isSecret: false,
			valueType: "integer");

		await factory.SeedApplicationAssignmentAsync(applicationId, environmentId, nodeGroupId: nodeGroupId);
		await factory.SeedDraftValueAsync(configItemId, ResourceScopeType.Application, applicationId, "3", isSecret: false);

		using var operatorClient = await factory.CreateAuthenticatedClientAsync(
			PermissionCatalog.AgentsWrite,
			PermissionCatalog.ConfigPublish);

		var tokenResponse = await operatorClient.PostAsync($"/api/v1/nodes/{managedNodeId}/enrollment-token", content: null);
		var token = await tokenResponse.Content.ReadFromJsonAsync<IssueAgentEnrollmentTokenResponse>();
		Assert.NotNull(token);

		using var agentClient = factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			HandleCookies = false
		});

		var enrollResponse = await agentClient.PostAsJsonAsync(
			"/api/v1/agent/enroll",
			new AgentEnrollRequest
			{
				ManagedNodeId = managedNodeId,
				Hostname = "node01.local",
				Platform = "linux",
				AgentVersion = "1.2.3",
				EnrollmentToken = token!.EnrollmentToken
			});
		var enrollment = await enrollResponse.Content.ReadFromJsonAsync<AgentEnrollmentResponse>();
		Assert.NotNull(enrollment);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
		var invalidationHub = factory.Services.GetRequiredService<IAgentInvalidationHub>();
		var notificationTask = ReadFirstNotificationAsync(invalidationHub, enrollment!.AgentId, cts.Token);

		var publishResponse = await operatorClient.PostAsJsonAsync(
			"/api/v1/publishes",
			new CreatePublishRequest
			{
				ApplicationId = applicationId,
				EnvironmentId = environmentId,
				ChangeSummary = "Initial publish",
				RolloutPolicy = "immediate"
			});
		var publish = await publishResponse.Content.ReadFromJsonAsync<CreatePublishResponse>();
		Assert.NotNull(publish);

		var notification = await notificationTask;
		Assert.Equal(enrollment.AgentId, notification.AgentId);
		Assert.Equal(applicationId, notification.ApplicationId);
		Assert.Equal(publish!.PublishedVersion.PublishedVersionId, notification.PublishedVersionId);
		Assert.Equal(publish.PublishedVersion.VersionNumber, notification.VersionNumber);
	}

	private static async Task<AgentInvalidationNotification> ReadFirstNotificationAsync(
		IAgentInvalidationHub invalidationHub,
		Guid agentId,
		CancellationToken cancellationToken)
	{
		await foreach (var notification in invalidationHub.SubscribeAsync(agentId, cancellationToken))
		{
			return notification;
		}

		throw new InvalidOperationException($"No notification was published for agent '{agentId}'.");
	}

	private sealed class AgentSynchronizationApiFactory : WebApplicationFactory<Program>
	{
		private const string TestPassword = "Passw0rd!Passw0rd!";
		private readonly string databaseName = $"secretmanager-agent-sync-tests-{Guid.NewGuid():N}";
		private readonly TestAgentSnapshotCache snapshotCache;

		public AgentSynchronizationApiFactory()
			: this(new TestAgentSnapshotCache(true))
		{
		}

		public AgentSynchronizationApiFactory(TestAgentSnapshotCache snapshotCache)
		{
			this.snapshotCache = snapshotCache;
			System.Environment.SetEnvironmentVariable(
				"ConnectionStrings__Postgres",
				"Host=localhost;Port=5432;Database=secretmanager_test;Username=secretmanager;Password=secretmanager");
			System.Environment.SetEnvironmentVariable(
				"Infrastructure__Redis__Configuration",
				"localhost:6380,password=secretmanager,abortConnect=false");
			System.Environment.SetEnvironmentVariable(
				"Infrastructure__Redis__InstanceName",
				"secretmanager:");
			System.Environment.SetEnvironmentVariable("Telemetry__EnableConsoleExporter", "false");
		}

		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.UseEnvironment("Test");
			builder.ConfigureAppConfiguration((_, configurationBuilder) =>
			{
				configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=secretmanager_test;Username=secretmanager;Password=secretmanager",
					["Infrastructure:Redis:Configuration"] = "localhost:6380,password=secretmanager,abortConnect=false",
					["Infrastructure:Redis:InstanceName"] = "secretmanager:",
					["Telemetry:EnableConsoleExporter"] = "false"
				});
			});

			builder.ConfigureServices(services =>
			{
				services.RemoveAll(typeof(IDbContextOptionsConfiguration<SecretManagerDbContext>));
				services.RemoveAll(typeof(DbContextOptions<SecretManagerDbContext>));
				services.RemoveAll(typeof(SecretManagerDbContext));
				services.AddDbContext<SecretManagerDbContext>(options => options.UseInMemoryDatabase(databaseName));
				services.RemoveAll(typeof(IAgentSnapshotCache));
				services.AddSingleton<IAgentSnapshotCache>(snapshotCache);
			});
		}

		public async Task<HttpClient> CreateAuthenticatedClientAsync(params string[] permissions)
		{
			await SeedUserAsync(permissions);

			var client = CreateClient(new WebApplicationFactoryClientOptions
			{
				AllowAutoRedirect = false,
				HandleCookies = true
			});

			var loginResponse = await client.PostAsJsonAsync(
				"/api/v1/auth/login",
				new LoginRequest
				{
					Username = "operator",
					Password = TestPassword
				});

			Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
			return client;
		}

		public async Task<Guid> SeedEnvironmentAsync(string name, string slug)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			var environment = new EnvironmentDefinition
			{
				Id = Guid.NewGuid(),
				Name = name,
				Slug = slug,
				Description = string.Empty,
				CreatedAtUtc = DateTimeOffset.UtcNow,
				UpdatedAtUtc = DateTimeOffset.UtcNow
			};

			dbContext.Environments.Add(environment);
			await dbContext.SaveChangesAsync();
			return environment.Id;
		}

		public async Task<Guid> SeedNodeGroupAsync(Guid environmentId, string name, string slug)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			var nodeGroup = new NodeGroupDefinition
			{
				Id = Guid.NewGuid(),
				EnvironmentId = environmentId,
				Name = name,
				Slug = slug,
				Description = string.Empty,
				CreatedAtUtc = DateTimeOffset.UtcNow,
				UpdatedAtUtc = DateTimeOffset.UtcNow
			};

			dbContext.NodeGroups.Add(nodeGroup);
			await dbContext.SaveChangesAsync();
			return nodeGroup.Id;
		}

		public async Task<Guid> SeedManagedNodeAsync(Guid environmentId, Guid? nodeGroupId, string name, string hostname)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			var managedNode = new ManagedNodeRecord
			{
				Id = Guid.NewGuid(),
				EnvironmentId = environmentId,
				NodeGroupId = nodeGroupId,
				Name = name,
				Hostname = hostname,
				Platform = "linux",
				Status = "Pending",
				AgentVersion = "0.0.0",
				RolloutPolicyDefault = "immediate",
				CreatedAtUtc = DateTimeOffset.UtcNow,
				UpdatedAtUtc = DateTimeOffset.UtcNow
			};

			dbContext.ManagedNodes.Add(managedNode);
			await dbContext.SaveChangesAsync();
			return managedNode.Id;
		}

		public async Task<Guid> SeedApplicationAsync(string name, string slug)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			var application = new ApplicationDefinition
			{
				Id = Guid.NewGuid(),
				Name = name,
				Slug = slug,
				Description = string.Empty,
				DefaultIntegrationMode = "runtime-api",
				CreatedAtUtc = DateTimeOffset.UtcNow,
				UpdatedAtUtc = DateTimeOffset.UtcNow
			};

			dbContext.Applications.Add(application);
			await dbContext.SaveChangesAsync();
			return application.Id;
		}

		public async Task<Guid> SeedNamespaceAsync(Guid applicationId, string name, string path)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			var catalogNamespace = new NamespaceDefinition
			{
				Id = Guid.NewGuid(),
				ApplicationId = applicationId,
				Name = name,
				Path = path,
				Description = string.Empty,
				CreatedAtUtc = DateTimeOffset.UtcNow,
				UpdatedAtUtc = DateTimeOffset.UtcNow
			};

			dbContext.Namespaces.Add(catalogNamespace);
			await dbContext.SaveChangesAsync();
			return catalogNamespace.Id;
		}

		public async Task<Guid> SeedConfigItemAsync(Guid applicationId, Guid namespaceId, string key, string fullPath, bool isSecret, string valueType)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			var configItem = new ConfigItemDefinition
			{
				Id = Guid.NewGuid(),
				ApplicationId = applicationId,
				NamespaceId = namespaceId,
				Key = key,
				FullPath = fullPath,
				ValueType = valueType,
				IsSecret = isSecret,
				IsRequired = false,
				DefaultRolloutPolicy = "immediate",
				ValidationSchemaJson = string.Empty,
				Description = string.Empty,
				CreatedAtUtc = DateTimeOffset.UtcNow,
				UpdatedAtUtc = DateTimeOffset.UtcNow
			};

			dbContext.ConfigItems.Add(configItem);
			await dbContext.SaveChangesAsync();
			return configItem.Id;
		}

		public async Task<Guid> SeedApplicationAssignmentAsync(Guid applicationId, Guid environmentId, Guid? nodeGroupId = null, Guid? managedNodeId = null)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			var assignment = new ApplicationAssignment
			{
				Id = Guid.NewGuid(),
				ApplicationId = applicationId,
				EnvironmentId = environmentId,
				NodeGroupId = nodeGroupId,
				ManagedNodeId = managedNodeId,
				Enabled = true,
				CreatedAtUtc = DateTimeOffset.UtcNow
			};

			dbContext.ApplicationAssignments.Add(assignment);
			await dbContext.SaveChangesAsync();
			return assignment.Id;
		}

		public async Task<Guid> SeedDraftValueAsync(Guid configItemId, ResourceScopeType scopeType, Guid scopeId, string valueJson, bool isSecret)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			var draftValueProtector = scope.ServiceProvider.GetRequiredService<IDraftValueProtector>();
			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			var draftValue = new DraftValue
			{
				Id = Guid.NewGuid(),
				ConfigItemId = configItemId,
				ScopeType = scopeType,
				ScopeId = scopeId,
				ValueJson = isSecret ? draftValueProtector.Protect(valueJson) : valueJson,
				IsSecret = isSecret,
				ChangeNote = "Seeded draft",
				UpdatedAtUtc = DateTimeOffset.UtcNow
			};

			dbContext.DraftValues.Add(draftValue);
			await dbContext.SaveChangesAsync();
			return draftValue.Id;
		}

		private async Task SeedUserAsync(IReadOnlyCollection<string> permissions)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			var passwordHasher = scope.ServiceProvider.GetRequiredService<Argon2PasswordHasher>();

			await dbContext.Database.EnsureCreatedAsync();
			await EnsureInstallationAsync(dbContext);

			if (await dbContext.Users.AnyAsync(x => x.Username == "operator"))
			{
				return;
			}

			var now = DateTimeOffset.UtcNow;
			var role = new RoleDefinition
			{
				Id = Guid.NewGuid(),
				Name = "TestAgentSyncOperator",
				Description = "Test-only agent synchronization role.",
				IsSystem = false,
				CreatedAtUtc = now
			};

			foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
			{
				role.Permissions.Add(new RolePermission
				{
					RoleDefinitionId = role.Id,
					Permission = permission
				});
			}

			var user = new UserAccount
			{
				Id = Guid.NewGuid(),
				Username = "operator",
				DisplayName = "Test Operator",
				PasswordHash = passwordHasher.Hash(TestPassword),
				Role = role.Name,
				IsEnabled = true,
				CreatedAtUtc = now
			};

			dbContext.RoleDefinitions.Add(role);
			dbContext.Users.Add(user);
			dbContext.RoleAssignments.Add(new RoleAssignment
			{
				Id = Guid.NewGuid(),
				UserId = user.Id,
				RoleDefinitionId = role.Id,
				ScopeType = ResourceScopeType.Installation,
				ScopeId = Installation.SingletonId,
				CreatedByUserId = user.Id,
				CreatedAtUtc = now
			});

			await dbContext.SaveChangesAsync();
		}

		private static async Task EnsureInstallationAsync(SecretManagerDbContext dbContext)
		{
			if (await dbContext.Installations.IgnoreQueryFilters().AnyAsync())
			{
				return;
			}

			dbContext.Installations.Add(new Installation
			{
				Id = Installation.SingletonId,
				Name = "Test Installation",
				CreatedAtUtc = DateTimeOffset.UtcNow,
				InitializedAtUtc = DateTimeOffset.UtcNow
			});
		}
	}

	private sealed class TestAgentSnapshotCache(bool available) : IAgentSnapshotCache
	{
		private readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);

		public Task SetAsync(string snapshotId, string payloadJson, CancellationToken cancellationToken)
		{
			if (!available)
			{
				throw new InvalidOperationException("Redis unavailable.");
			}

			entries[snapshotId] = payloadJson;
			return Task.CompletedTask;
		}

		public Task<string?> GetAsync(string snapshotId, CancellationToken cancellationToken)
		{
			if (!available)
			{
				throw new InvalidOperationException("Redis unavailable.");
			}

			entries.TryGetValue(snapshotId, out var payloadJson);
			return Task.FromResult(payloadJson);
		}
	}
}