using Microsoft.EntityFrameworkCore;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Installations;
using SecretManager.Infrastructure.Persistence;

namespace SecretManager.Infrastructure.Authorization;

internal static class SystemAuthorizationSeeder
{
    public static async Task SeedAsync(
        SecretManagerDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var roles = await dbContext.RoleDefinitions
            .Include(x => x.Permissions)
            .ToListAsync(cancellationToken);

        var rolesByName = roles.ToDictionary(x => x.Name, StringComparer.Ordinal);

        foreach (var systemRole in SystemRoleCatalog.Roles)
        {
            if (!rolesByName.TryGetValue(systemRole.Name, out var role))
            {
                role = new RoleDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = systemRole.Name,
                    Description = systemRole.Description,
                    IsSystem = true,
                    CreatedAtUtc = now
                };

                foreach (var permission in systemRole.Permissions.Distinct(StringComparer.Ordinal))
                {
                    role.Permissions.Add(new RolePermission
                    {
                        RoleDefinitionId = role.Id,
                        Permission = permission
                    });
                }

                dbContext.RoleDefinitions.Add(role);
                rolesByName.Add(role.Name, role);
                continue;
            }

            role.Description = systemRole.Description;
            role.IsSystem = true;

            var existingPermissions = role.Permissions
                .Select(x => x.Permission)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var permission in systemRole.Permissions)
            {
                if (existingPermissions.Add(permission))
                {
                    role.Permissions.Add(new RolePermission
                    {
                        RoleDefinitionId = role.Id,
                        Permission = permission
                    });
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var legacyUsers = await dbContext.Users
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.Role))
            .ToListAsync(cancellationToken);

        var existingAssignments = await dbContext.RoleAssignments
            .AsNoTracking()
            .Where(x => x.ScopeType == ResourceScopeType.Installation && x.ScopeId == Installation.SingletonId)
            .Select(x => new { x.UserId, x.RoleDefinitionId })
            .ToListAsync(cancellationToken);

        var existingAssignmentSet = existingAssignments
            .Select(x => (x.UserId, x.RoleDefinitionId))
            .ToHashSet();

        foreach (var user in legacyUsers)
        {
            if (!rolesByName.TryGetValue(user.Role, out var role))
            {
                continue;
            }

            var assignmentKey = (user.Id, role.Id);
            if (!existingAssignmentSet.Add(assignmentKey))
            {
                continue;
            }

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
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}