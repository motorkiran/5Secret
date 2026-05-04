namespace SecretManager.Shared.Contracts.Audit;

public sealed record AuditEventSummaryResponse(
    Guid EventId,
    string Action,
    string Outcome,
    string TargetType,
    string TargetIdentifier,
    string TargetDisplayName,
    Guid? ActorUserId,
    string? ActorUsername,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

public sealed record AuditEventDetailResponse(
    Guid EventId,
    string Action,
    string Outcome,
    string TargetType,
    string TargetIdentifier,
    string TargetDisplayName,
    Guid? ActorUserId,
    string? ActorUsername,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? RemoteIpAddress,
    string? DetailsJson);