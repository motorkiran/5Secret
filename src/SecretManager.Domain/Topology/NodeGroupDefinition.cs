namespace SecretManager.Domain.Topology;

public sealed class NodeGroupDefinition
{
    public Guid Id { get; set; }

    public Guid EnvironmentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}