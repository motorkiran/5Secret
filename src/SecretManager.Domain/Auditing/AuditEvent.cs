using SecretManager.Domain.Installations;

namespace SecretManager.Domain.Auditing;

public sealed class AuditEvent
{
    public Guid Id { get; set; }

    public Guid InstallationId { get; set; } = Installation.SingletonId;

    public Guid? ActorUserId { get; set; }

    public string? ActorUsername { get; set; }

    public string Action { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public string TargetIdentifier { get; set; } = string.Empty;

    public string TargetDisplayName { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string? RemoteIpAddress { get; set; }

    public string? DetailsJson { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}