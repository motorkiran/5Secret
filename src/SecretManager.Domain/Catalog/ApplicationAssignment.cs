namespace SecretManager.Domain.Catalog;

public sealed class ApplicationAssignment
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    public Guid EnvironmentId { get; set; }

    public Guid? NodeGroupId { get; set; }

    public Guid? ManagedNodeId { get; set; }

    public bool Enabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}