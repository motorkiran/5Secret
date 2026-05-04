namespace SecretManager.Domain.Catalog;

public sealed class ApplicationDefinition
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DefaultIntegrationMode { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}