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
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Environments;
using SecretManager.Domain.Installations;
using SecretManager.Domain.Topology;
using SecretManager.Domain.Users;
using SecretManager.Infrastructure.Persistence;
using SecretManager.Infrastructure.Security;
using SecretManager.Shared.Contracts.Auth;
using SecretManager.Shared.Contracts.Topology;

namespace SecretManager.Infrastructure.Tests.Api;

public sealed class NodeRegistryEndpointsTests
{
    [Fact]
    public async Task CreateAndListNodeGroups_Succeed_WhenUserHasNodeGroupPermissions()
    {
        using var factory = new NodeRegistryApiFactory();
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.NodeGroupsRead,
            PermissionCatalog.NodeGroupsWrite);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/node-groups",
            new CreateNodeGroupRequest
            {
                EnvironmentId = environmentId,
                Name = "Backend Nodes",
                Description = "Primary backend node group"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdNodeGroup = await createResponse.Content.ReadFromJsonAsync<NodeGroupSummaryResponse>();
        Assert.NotNull(createdNodeGroup);
        Assert.Equal(environmentId, createdNodeGroup!.EnvironmentId);
        Assert.Equal("backend-nodes", createdNodeGroup.Slug);

        var nodeGroups = await client.GetFromJsonAsync<List<NodeGroupSummaryResponse>>($"/api/v1/node-groups?environmentId={environmentId}");
        Assert.NotNull(nodeGroups);

        var listedNodeGroup = Assert.Single(nodeGroups!);
        Assert.Equal(createdNodeGroup.NodeGroupId, listedNodeGroup.NodeGroupId);
    }

    [Fact]
    public async Task CreateAndListNodes_Succeed_WhenUserHasNodePermissions()
    {
        using var factory = new NodeRegistryApiFactory();
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        var nodeGroupId = await factory.SeedNodeGroupAsync(environmentId, "Backend Nodes", "backend-nodes");
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.NodesRead,
            PermissionCatalog.NodesWrite);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/nodes",
            new CreateManagedNodeRequest
            {
                EnvironmentId = environmentId,
                NodeGroupId = nodeGroupId,
                Name = "Node 01",
                Hostname = "node01.local",
                Platform = "linux",
                Status = "Online",
                AgentVersion = "1.0.0"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdNode = await createResponse.Content.ReadFromJsonAsync<ManagedNodeSummaryResponse>();
        Assert.NotNull(createdNode);
        Assert.Equal(environmentId, createdNode!.EnvironmentId);
        Assert.Equal(nodeGroupId, createdNode.NodeGroupId);
        Assert.Equal("node01.local", createdNode.Hostname);
        Assert.Equal("Online", createdNode.Status);

        var nodes = await client.GetFromJsonAsync<List<ManagedNodeSummaryResponse>>($"/api/v1/nodes?environmentId={environmentId}");
        Assert.NotNull(nodes);

        var listedNode = Assert.Single(nodes!);
        Assert.Equal(createdNode.NodeId, listedNode.NodeId);
    }

    [Fact]
    public async Task CreateManagedNode_ReturnsForbidden_WhenUserLacksWritePermission()
    {
        using var factory = new NodeRegistryApiFactory();
        var environmentId = await factory.SeedEnvironmentAsync("Production", "production");
        using var client = await factory.CreateAuthenticatedClientAsync(PermissionCatalog.NodesRead);

        var response = await client.PostAsJsonAsync(
            "/api/v1/nodes",
            new CreateManagedNodeRequest
            {
                EnvironmentId = environmentId,
                Name = "Node 01",
                Hostname = "node01.local"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class NodeRegistryApiFactory : WebApplicationFactory<Program>
    {
        private const string TestPassword = "Passw0rd!Passw0rd!";
        private readonly string databaseName = $"secretmanager-node-registry-tests-{Guid.NewGuid():N}";

        public NodeRegistryApiFactory()
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
            await EnsureInstallationAsync(dbContext);
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
                Name = "TestNodeRegistryOperator",
                Description = "Test-only node registry role.",
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