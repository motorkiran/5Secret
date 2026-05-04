using System.ComponentModel.DataAnnotations;

namespace SecretManager.Shared.Contracts.Topology;

public sealed class CreateNodeGroupRequest
{
    public Guid EnvironmentId { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Slug { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;
}

public sealed class UpdateNodeGroupRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Slug { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;
}

public sealed record NodeGroupSummaryResponse(
    Guid NodeGroupId,
    Guid EnvironmentId,
    string Name,
    string Slug,
    string Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class CreateManagedNodeRequest
{
    public Guid EnvironmentId { get; set; }

    public Guid? NodeGroupId { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(256)]
    public string Hostname { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Platform { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? LastSeenAtUtc { get; set; }

    [MaxLength(64)]
    public string AgentVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string RolloutPolicyDefault { get; set; } = string.Empty;
}

public sealed class UpdateManagedNodeRequest
{
    public Guid? NodeGroupId { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(256)]
    public string Hostname { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Platform { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? LastSeenAtUtc { get; set; }

    [MaxLength(64)]
    public string AgentVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string RolloutPolicyDefault { get; set; } = string.Empty;
}

public sealed record ManagedNodeSummaryResponse(
    Guid NodeId,
    Guid EnvironmentId,
    Guid? NodeGroupId,
    string Name,
    string Hostname,
    string Platform,
    string Status,
    DateTimeOffset? LastSeenAtUtc,
    string AgentVersion,
    string RolloutPolicyDefault,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);