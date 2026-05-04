using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecretManager.ControlPlane.Application.Authorization;
using SecretManager.Domain.Agents;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Environments;
using SecretManager.Domain.Installations;
using SecretManager.Domain.Topology;
using SecretManager.Domain.Users;
using SecretManager.Infrastructure.Persistence;
using SecretManager.Infrastructure.Security;
using SecretManager.Shared.Contracts.Agents;
using SecretManager.Shared.Contracts.Auth;

namespace SecretManager.Infrastructure.Tests.Api;

public sealed class AgentControlEndpointsTests
{
	[Fact]
	public async Task AgentEnrollment_SucceedsOnce_AndRejectsReuseOrInvalidTokens()
	{
		using var factory = new AgentControlApiFactory();
		var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
		var nodeGroupId = await factory.SeedNodeGroupAsync(environmentId, "Backend", "backend");
		var managedNodeId = await factory.SeedManagedNodeAsync(environmentId, nodeGroupId, "Node 01", "node01.local");
		var otherNodeId = await factory.SeedManagedNodeAsync(environmentId, nodeGroupId, "Node 02", "node02.local");

		using var operatorClient = await factory.CreateAuthenticatedClientAsync(PermissionCatalog.AgentsWrite);
		var tokenResponse = await operatorClient.PostAsync($"/api/v1/nodes/{managedNodeId}/enrollment-token", content: null);
		var tokenResponseBody = await tokenResponse.Content.ReadAsStringAsync();
		Assert.True(tokenResponse.StatusCode == HttpStatusCode.OK, tokenResponseBody);
		var issuedToken = await tokenResponse.Content.ReadFromJsonAsync<IssueAgentEnrollmentTokenResponse>();
		Assert.NotNull(issuedToken);
		Assert.Equal(managedNodeId, issuedToken!.ManagedNodeId);
		Assert.False(string.IsNullOrWhiteSpace(issuedToken.EnrollmentToken));

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
				EnrollmentToken = issuedToken.EnrollmentToken
			});

		var enrollResponseBody = await enrollResponse.Content.ReadAsStringAsync();
		Assert.True(enrollResponse.StatusCode == HttpStatusCode.OK, enrollResponseBody);
		var enrollment = await enrollResponse.Content.ReadFromJsonAsync<AgentEnrollmentResponse>();
		Assert.NotNull(enrollment);
		Assert.Equal(managedNodeId, enrollment!.ManagedNodeId);
		Assert.Equal(environmentId, enrollment.EnvironmentId);
		Assert.False(string.IsNullOrWhiteSpace(enrollment.AgentCredential));
		Assert.False(string.IsNullOrWhiteSpace(enrollment.EnrollmentSecret));
		Assert.Empty(enrollment.InitialAssignments);

		using (var scope = factory.Services.CreateScope())
		{
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			var agentRegistration = await dbContext.AgentRegistrations.AsNoTracking().SingleAsync();
			var enrollmentToken = await dbContext.AgentEnrollmentTokens.AsNoTracking().SingleAsync();
			var managedNode = await dbContext.ManagedNodes.AsNoTracking().SingleAsync(x => x.Id == managedNodeId);

			Assert.Equal(enrollment.AgentId, agentRegistration.Id);
			Assert.Equal(managedNodeId, agentRegistration.ManagedNodeId);
			Assert.True(enrollmentToken.ConsumedAtUtc.HasValue);
			Assert.Equal(agentRegistration.Id, enrollmentToken.ConsumedByAgentId);
			Assert.Equal("Enrolled", managedNode.Status);
			Assert.Equal("1.2.3", managedNode.AgentVersion);
			Assert.Equal("linux", managedNode.Platform);
		}

		var reuseResponse = await agentClient.PostAsJsonAsync(
			"/api/v1/agent/enroll",
			new AgentEnrollRequest
			{
				ManagedNodeId = managedNodeId,
				Hostname = "node01.local",
				Platform = "linux",
				AgentVersion = "1.2.3",
				EnrollmentToken = issuedToken.EnrollmentToken
			});
		Assert.Equal(HttpStatusCode.Conflict, reuseResponse.StatusCode);

		var invalidResponse = await agentClient.PostAsJsonAsync(
			"/api/v1/agent/enroll",
			new AgentEnrollRequest
			{
				ManagedNodeId = otherNodeId,
				Hostname = "node02.local",
				Platform = "linux",
				AgentVersion = "1.2.3",
				EnrollmentToken = "invalid-enrollment-token-value"
			});
		Assert.Equal(HttpStatusCode.Forbidden, invalidResponse.StatusCode);

		using var auditScope = factory.Services.CreateScope();
		var auditDbContext = auditScope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
		var enrollmentAuditEvents = await auditDbContext.AuditEvents
			.AsNoTracking()
			.Where(x => x.Action == "agent.enrolled")
			.ToListAsync();

		Assert.Single(enrollmentAuditEvents);
	}

	[Fact]
	public async Task AgentHeartbeat_UpdatesStatus_AndStaleAgentsAreReportedAsDegraded()
	{
		using var factory = new AgentControlApiFactory();
		var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
		var nodeGroupId = await factory.SeedNodeGroupAsync(environmentId, "Backend", "backend");
		var managedNodeId = await factory.SeedManagedNodeAsync(environmentId, nodeGroupId, "Node 01", "node01.local");

		using var operatorClient = await factory.CreateAuthenticatedClientAsync(
			PermissionCatalog.AgentsWrite,
			PermissionCatalog.AgentsRead);
		var tokenResponse = await operatorClient.PostAsync($"/api/v1/nodes/{managedNodeId}/enrollment-token", content: null);
		var issuedToken = await tokenResponse.Content.ReadFromJsonAsync<IssueAgentEnrollmentTokenResponse>();
		Assert.NotNull(issuedToken);

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
				EnrollmentToken = issuedToken!.EnrollmentToken
			});
		var enrollment = await enrollResponse.Content.ReadFromJsonAsync<AgentEnrollmentResponse>();
		Assert.NotNull(enrollment);

		var heartbeatResponse = await agentClient.PostAsJsonAsync(
			"/api/v1/agent/heartbeat",
			new AgentHeartbeatRequest
			{
				AgentId = enrollment!.AgentId,
				AgentCredential = enrollment.AgentCredential,
				AgentVersion = "1.2.3",
				CurrentVersionNumber = 7
			});
		var heartbeatBody = await heartbeatResponse.Content.ReadAsStringAsync();
		Assert.True(heartbeatResponse.StatusCode == HttpStatusCode.OK, heartbeatBody);
		var heartbeat = await heartbeatResponse.Content.ReadFromJsonAsync<AgentStatusResponse>();
		Assert.NotNull(heartbeat);
		Assert.Equal("Online", heartbeat!.HealthStatus);
		Assert.Equal(7, heartbeat.CurrentVersionNumber);

		var statusResponse = await operatorClient.GetAsync($"/api/v1/agents/{enrollment.AgentId}/status");
		var statusBody = await statusResponse.Content.ReadAsStringAsync();
		Assert.True(statusResponse.StatusCode == HttpStatusCode.OK, statusBody);
		var status = await statusResponse.Content.ReadFromJsonAsync<AgentStatusResponse>();
		Assert.NotNull(status);
		Assert.Equal("Online", status!.HealthStatus);
		Assert.NotNull(status.LastSeenAtUtc);
		Assert.Equal(7, status.CurrentVersionNumber);

		await factory.SetAgentLastSeenAsync(enrollment.AgentId, DateTimeOffset.UtcNow.AddMinutes(-10));

		var degradedStatusResponse = await operatorClient.GetAsync($"/api/v1/agents/{enrollment.AgentId}/status");
		var degradedStatus = await degradedStatusResponse.Content.ReadFromJsonAsync<AgentStatusResponse>();
		Assert.NotNull(degradedStatus);
		Assert.Equal("Degraded", degradedStatus!.HealthStatus);

		var agentsResponse = await operatorClient.GetAsync($"/api/v1/agents?environmentId={environmentId}");
		var agents = await agentsResponse.Content.ReadFromJsonAsync<List<AgentStatusResponse>>();
		Assert.NotNull(agents);
		var listedAgent = Assert.Single(agents!);
		Assert.Equal(enrollment.AgentId, listedAgent.AgentId);
		Assert.Equal("Degraded", listedAgent.HealthStatus);
	}

	private sealed class AgentControlApiFactory : WebApplicationFactory<Program>
	{
		private const string TestPassword = "Passw0rd!Passw0rd!";
		private readonly string databaseName = $"secretmanager-agent-control-tests-{Guid.NewGuid():N}";

		public AgentControlApiFactory()
		{
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

		public async Task SetAgentLastSeenAsync(Guid agentId, DateTimeOffset lastSeenAtUtc)
		{
			using var scope = Services.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
			var agentRegistration = await dbContext.AgentRegistrations.FirstAsync(x => x.Id == agentId);
			var managedNode = await dbContext.ManagedNodes.FirstAsync(x => x.Id == agentRegistration.ManagedNodeId);

			agentRegistration.LastSeenAtUtc = lastSeenAtUtc;
			agentRegistration.UpdatedAtUtc = DateTimeOffset.UtcNow;
			managedNode.LastSeenAtUtc = lastSeenAtUtc;
			managedNode.UpdatedAtUtc = DateTimeOffset.UtcNow;

			await dbContext.SaveChangesAsync();
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
				Name = "TestAgentOperator",
				Description = "Test-only agent control role.",
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
}