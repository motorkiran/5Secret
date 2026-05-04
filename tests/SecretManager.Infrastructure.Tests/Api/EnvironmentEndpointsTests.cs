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
using SecretManager.Domain.Installations;
using SecretManager.Domain.Users;
using SecretManager.Infrastructure.Persistence;
using SecretManager.Infrastructure.Security;
using SecretManager.Shared.Contracts.Auth;
using SecretManager.Shared.Contracts.Environments;

namespace SecretManager.Infrastructure.Tests.Api;

public sealed class EnvironmentEndpointsTests
{
    [Fact]
    public async Task GetEnvironments_ReturnsUnauthorized_WhenUserIsAnonymous()
    {
        using var factory = new EnvironmentApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var response = await client.GetAsync("/api/v1/environments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndListEnvironments_Succeed_WhenUserHasEnvironmentPermissions()
    {
        using var factory = new EnvironmentApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            PermissionCatalog.EnvironmentsRead,
            PermissionCatalog.EnvironmentsWrite);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/environments",
            new CreateEnvironmentRequest
            {
                Name = "Production",
                Description = "Primary environment",
                IsProtected = true
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdEnvironment = await createResponse.Content.ReadFromJsonAsync<EnvironmentSummaryResponse>();
        Assert.NotNull(createdEnvironment);
        Assert.Equal("production", createdEnvironment!.Slug);
        Assert.True(createdEnvironment.IsProtected);

        var environments = await client.GetFromJsonAsync<List<EnvironmentSummaryResponse>>("/api/v1/environments");
        Assert.NotNull(environments);

        var listedEnvironment = Assert.Single(environments!);
        Assert.Equal(createdEnvironment.EnvironmentId, listedEnvironment.EnvironmentId);
        Assert.Equal("Production", listedEnvironment.Name);
    }

    [Fact]
    public async Task CreateEnvironment_ReturnsForbidden_WhenUserLacksWritePermission()
    {
        using var factory = new EnvironmentApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(PermissionCatalog.EnvironmentsRead);

        var response = await client.PostAsJsonAsync(
            "/api/v1/environments",
            new CreateEnvironmentRequest
            {
                Name = "Production"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class EnvironmentApiFactory : WebApplicationFactory<Program>
    {
        private const string TestPassword = "Passw0rd!Passw0rd!";
        private readonly string databaseName = $"secretmanager-api-tests-{Guid.NewGuid():N}";

        public EnvironmentApiFactory()
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

        private async Task SeedUserAsync(IReadOnlyCollection<string> permissions)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<Argon2PasswordHasher>();

            await dbContext.Database.EnsureCreatedAsync();

            if (await dbContext.Users.AnyAsync(x => x.Username == "operator"))
            {
                return;
            }

            if (!await dbContext.Installations.IgnoreQueryFilters().AnyAsync())
            {
                dbContext.Installations.Add(new Installation
                {
                    Id = Installation.SingletonId,
                    Name = "Test Installation",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    InitializedAtUtc = DateTimeOffset.UtcNow
                });
            }

            var now = DateTimeOffset.UtcNow;
            var role = new RoleDefinition
            {
                Id = Guid.NewGuid(),
                Name = "TestEnvironmentOperator",
                Description = "Test-only environment operator role.",
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
    }
}