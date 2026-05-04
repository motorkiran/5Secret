using System.Text.Json;
using SecretManager.Domain.Auditing;
using SecretManager.Domain.Installations;
using SecretManager.Infrastructure.Persistence;

namespace SecretManager.Infrastructure.Auditing;

public interface IAuditEventWriter
{
    AuditEvent Create(AuditEventWriteRequest request);

    Task<Guid> WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken);
}

public sealed record AuditEventWriteRequest(
    string Action,
    string TargetType,
    string TargetIdentifier,
    string? TargetDisplayName,
    string Outcome,
    string CorrelationId,
    Guid? ActorUserId = null,
    string? ActorUsername = null,
    string? RemoteIpAddress = null,
    IReadOnlyDictionary<string, object?>? Details = null,
    Guid? InstallationId = null,
    DateTimeOffset? OccurredAtUtc = null);

public sealed class AuditEventWriter(SecretManagerDbContext dbContext) : IAuditEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AuditEvent Create(AuditEventWriteRequest request)
    {
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            InstallationId = request.InstallationId ?? Installation.SingletonId,
            ActorUserId = request.ActorUserId,
            ActorUsername = NormalizeOptional(request.ActorUsername),
            Action = NormalizeRequired(request.Action, nameof(request.Action)),
            TargetType = NormalizeRequired(request.TargetType, nameof(request.TargetType)),
            TargetIdentifier = NormalizeRequired(request.TargetIdentifier, nameof(request.TargetIdentifier)),
            TargetDisplayName = NormalizeOptional(request.TargetDisplayName) ?? NormalizeRequired(request.TargetIdentifier, nameof(request.TargetIdentifier)),
            Outcome = NormalizeRequired(request.Outcome, nameof(request.Outcome)),
            CorrelationId = NormalizeRequired(request.CorrelationId, nameof(request.CorrelationId)),
            RemoteIpAddress = NormalizeOptional(request.RemoteIpAddress),
            DetailsJson = SerializeDetails(request.Details),
            OccurredAtUtc = request.OccurredAtUtc ?? DateTimeOffset.UtcNow
        };
    }

    public async Task<Guid> WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken)
    {
        var auditEvent = Create(request);

        dbContext.AuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(cancellationToken);

        return auditEvent.Id;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        var normalizedValue = value.Trim();
        if (normalizedValue.Length == 0)
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return normalizedValue;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? SerializeDetails(IReadOnlyDictionary<string, object?>? details)
    {
        if (details is null || details.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(details, JsonOptions);
    }
}