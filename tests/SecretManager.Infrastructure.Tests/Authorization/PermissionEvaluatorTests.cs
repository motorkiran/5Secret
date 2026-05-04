using Microsoft.EntityFrameworkCore;
using SecretManager.ControlPlane.Application.Authorization;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Installations;
using SecretManager.Infrastructure.Authorization;
using SecretManager.Infrastructure.Persistence;

namespace SecretManager.Infrastructure.Tests.Authorization;

public sealed class PermissionEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_Allows_WhenAncestorScopeAssignmentMatchesPath()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();

        SeedRoleAssignment(
            dbContext,
            userId,
            PermissionCatalog.ConfigReadMasked,
            ResourceScopeType.Installation,
            Installation.SingletonId);

        var evaluator = new PermissionEvaluator(dbContext);

        var result = await evaluator.EvaluateAsync(
            new PermissionEvaluationRequest(
                userId,
                PermissionCatalog.ConfigReadMasked,
                [
                    new ResourceScope(ResourceScopeType.Installation, Installation.SingletonId),
                    new ResourceScope(ResourceScopeType.Namespace, Guid.NewGuid())
                ]),
            CancellationToken.None);

        Assert.True(result.IsAllowed);
        Assert.Equal(ResourceScopeType.Installation, result.GrantedScopeType);
        Assert.Equal("TestRole", result.MatchedRoleName);
    }

    [Fact]
    public async Task EvaluateAsync_Denies_WhenPermissionIsMissing()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();

        SeedRoleAssignment(
            dbContext,
            userId,
            PermissionCatalog.UsersRead,
            ResourceScopeType.Installation,
            Installation.SingletonId);

        var evaluator = new PermissionEvaluator(dbContext);

        var result = await evaluator.EvaluateAsync(
            new PermissionEvaluationRequest(
                userId,
                PermissionCatalog.ConfigRevealSecret,
                [new ResourceScope(ResourceScopeType.Installation, Installation.SingletonId)]),
            CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Null(result.MatchedRoleName);
    }

    [Fact]
    public async Task EvaluateAsync_Denies_WhenScopePathDoesNotContainGrantedScope()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();

        SeedRoleAssignment(
            dbContext,
            userId,
            PermissionCatalog.UsersRead,
            ResourceScopeType.Application,
            Guid.NewGuid());

        var evaluator = new PermissionEvaluator(dbContext);

        var result = await evaluator.EvaluateAsync(
            new PermissionEvaluationRequest(
                userId,
                PermissionCatalog.UsersRead,
                [new ResourceScope(ResourceScopeType.Installation, Installation.SingletonId)]),
            CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Null(result.GrantedScopeType);
    }

    private static SecretManagerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SecretManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SecretManagerDbContext(options);
    }

    private static void SeedRoleAssignment(
        SecretManagerDbContext dbContext,
        Guid userId,
        string permission,
        ResourceScopeType grantedScopeType,
        Guid grantedScopeId)
    {
        var role = new RoleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "TestRole",
            Description = "Test role",
            IsSystem = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Permissions =
            [
                new RolePermission
                {
                    RoleDefinitionId = Guid.Empty,
                    Permission = permission
                }
            ]
        };

        role.Permissions.Single().RoleDefinitionId = role.Id;

        dbContext.RoleDefinitions.Add(role);
        dbContext.RoleAssignments.Add(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleDefinitionId = role.Id,
            ScopeType = grantedScopeType,
            ScopeId = grantedScopeId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RoleDefinition = role
        });

        dbContext.SaveChanges();
    }
}