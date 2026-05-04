namespace SecretManager.Domain.Catalog;

public sealed class NamespaceDefinition
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}