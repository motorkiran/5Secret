namespace SecretManager.Domain.Authorization;

public sealed class RolePermission
{
    public Guid RoleDefinitionId { get; set; }

    public string Permission { get; set; } = string.Empty;

    public RoleDefinition? RoleDefinition { get; set; }
}