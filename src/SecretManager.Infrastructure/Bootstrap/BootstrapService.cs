using Microsoft.EntityFrameworkCore;
using SecretManager.ControlPlane.Application.Bootstrap;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Installations;
using SecretManager.Domain.Users;
using SecretManager.Infrastructure.Auditing;
using SecretManager.Infrastructure.Persistence;
using SecretManager.Infrastructure.Security;

namespace SecretManager.Infrastructure.Bootstrap;

internal sealed class BootstrapService(
    SecretManagerDbContext dbContext,
    Argon2PasswordHasher passwordHasher,
    IAuditEventWriter auditEventWriter) : IBootstrapService
{
    private const string BootstrapOwnerRole = "BootstrapOwner";

    public async Task<BootstrapStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        var installation = await dbContext.Installations
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return new BootstrapStatusResult(installation is not null, installation?.Name);
    }

    public async Task<BootstrapInstallationResult> BootstrapAsync(
        BootstrapInstallationCommand command,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Installations.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("Installation is already initialized.");
        }

        var now = DateTimeOffset.UtcNow;
        var installation = new Installation
        {
            Id = Installation.SingletonId,
            Name = command.InstallationName.Trim(),
            CreatedAtUtc = now,
            InitializedAtUtc = now
        };

        var owner = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = command.OwnerUsername.Trim(),
            DisplayName = command.OwnerDisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(command.Password),
            Role = BootstrapOwnerRole,
            IsEnabled = true,
            CreatedAtUtc = now
        };

        var bootstrapRole = await dbContext.RoleDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == BootstrapOwnerRole, cancellationToken)
            ?? throw new InvalidOperationException("Bootstrap role is not available.");

        dbContext.Installations.Add(installation);
        dbContext.Users.Add(owner);
        dbContext.RoleAssignments.Add(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            RoleDefinitionId = bootstrapRole.Id,
            ScopeType = ResourceScopeType.Installation,
            ScopeId = installation.Id,
            CreatedByUserId = owner.Id,
            CreatedAtUtc = now
        });
        dbContext.AuditEvents.Add(
            auditEventWriter.Create(
                new AuditEventWriteRequest(
                    Action: "installation.bootstrap",
                    TargetType: "Installation",
                    TargetIdentifier: installation.Id.ToString(),
                    TargetDisplayName: installation.Name,
                    Outcome: "Succeeded",
                    CorrelationId: string.IsNullOrWhiteSpace(command.CorrelationId)
                        ? Guid.NewGuid().ToString("N")
                        : command.CorrelationId,
                    ActorUserId: owner.Id,
                    ActorUsername: owner.Username,
                    RemoteIpAddress: command.RemoteIpAddress,
                    Details: new Dictionary<string, object?>
                    {
                        ["ownerUsername"] = owner.Username
                    },
                    InstallationId: installation.Id,
                    OccurredAtUtc: now)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException("Installation is already initialized.", ex);
        }

        return new BootstrapInstallationResult(
            installation.Id,
            owner.Id,
            installation.Name,
            owner.Username);
    }
}