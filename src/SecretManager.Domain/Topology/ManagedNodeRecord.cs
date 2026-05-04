namespace SecretManager.Domain.Topology;

public sealed class ManagedNodeRecord
{
    public Guid Id { get; set; }

    public Guid EnvironmentId { get; set; }

    public Guid? NodeGroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Hostname { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? LastSeenAtUtc { get; set; }

    public string AgentVersion { get; set; } = string.Empty;

    public string RolloutPolicyDefault { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}