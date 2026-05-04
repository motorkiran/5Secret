using Microsoft.EntityFrameworkCore;
using SecretManager.ControlPlane.Application.Authorization;
using SecretManager.Infrastructure.Persistence;

namespace SecretManager.Infrastructure.Authorization;

internal sealed class PermissionEvaluator(SecretManagerDbContext dbContext) : IPermissionEvaluator
{
    public async Task<PermissionEvaluationResult> EvaluateAsync(
        PermissionEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var permission = request.Permission.Trim();
        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(permission) || request.ScopePath.Count == 0)
        {
            return new PermissionEvaluationResult(false, null, null, null);
        }

        var scopePath = request.ScopePath
            .Where(x => x.ScopeId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (scopePath.Length == 0)
        {
            return new PermissionEvaluationResult(false, null, null, null);
        }

        var now = DateTimeOffset.UtcNow;
        var assignments = await dbContext.RoleAssignments
            .AsNoTracking()
            .Include(x => x.RoleDefinition!)
            .ThenInclude(x => x.Permissions)
            .Where(x => x.UserId == request.UserId)
            .Where(x => x.ExpiresAtUtc == null || x.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var assignment in assignments)
        {
            if (!scopePath.Any(x => x.ScopeType == assignment.ScopeType && x.ScopeId == assignment.ScopeId))
            {
                continue;
            }

            if (assignment.RoleDefinition?.Permissions.Any(x => x.Permission == permission) != true)
            {
                continue;
            }

            return new PermissionEvaluationResult(
                true,
                assignment.RoleDefinition.Name,
                assignment.ScopeType,
                assignment.ScopeId);
        }

        return new PermissionEvaluationResult(false, null, null, null);
    }
}