namespace SecretManager.Domain.Authorization;

public sealed class RoleDefinition
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
}