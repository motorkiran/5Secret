namespace SecretManager.Domain.Authorization;

public sealed class RoleAssignment
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid RoleDefinitionId { get; set; }

    public ResourceScopeType ScopeType { get; set; }

    public Guid ScopeId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public RoleDefinition? RoleDefinition { get; set; }
}