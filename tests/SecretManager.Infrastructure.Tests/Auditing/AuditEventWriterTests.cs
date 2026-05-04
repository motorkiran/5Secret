using Microsoft.EntityFrameworkCore;
using SecretManager.Infrastructure.Auditing;
using SecretManager.Infrastructure.Persistence;

namespace SecretManager.Infrastructure.Tests.Auditing;

public sealed class AuditEventWriterTests
{
    [Fact]
    public async Task WriteAsync_PersistsActorTargetAndCorrelationData()
    {
        await using var dbContext = CreateDbContext();
        var writer = new AuditEventWriter(dbContext);
        var occurredAtUtc = new DateTimeOffset(2026, 4, 23, 16, 0, 0, TimeSpan.Zero);

        var eventId = await writer.WriteAsync(
            new AuditEventWriteRequest(
                Action: "authorization.roleAssignment.created",
                TargetType: "RoleAssignment",
                TargetIdentifier: "assignment-1",
                TargetDisplayName: "Reader -> reader1",
                Outcome: "Succeeded",
                CorrelationId: "corr-123",
                ActorUserId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ActorUsername: "rootadmin",
                RemoteIpAddress: "127.0.0.1",
                Details: new Dictionary<string, object?>
                {
                    ["roleName"] = "Reader",
                    ["scopeType"] = "Installation"
                },
                OccurredAtUtc: occurredAtUtc),
            CancellationToken.None);

        var auditEvent = await dbContext.AuditEvents.SingleAsync();

        Assert.Equal(eventId, auditEvent.Id);
        Assert.Equal("authorization.roleAssignment.created", auditEvent.Action);
        Assert.Equal("RoleAssignment", auditEvent.TargetType);
        Assert.Equal("assignment-1", auditEvent.TargetIdentifier);
        Assert.Equal("Reader -> reader1", auditEvent.TargetDisplayName);
        Assert.Equal("Succeeded", auditEvent.Outcome);
        Assert.Equal("corr-123", auditEvent.CorrelationId);
        Assert.Equal("127.0.0.1", auditEvent.RemoteIpAddress);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), auditEvent.ActorUserId);
        Assert.Equal("rootadmin", auditEvent.ActorUsername);
        Assert.Equal(occurredAtUtc, auditEvent.OccurredAtUtc);
        Assert.Contains("\"roleName\":\"Reader\"", auditEvent.DetailsJson);
    }

    private static SecretManagerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SecretManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SecretManagerDbContext(options);
    }
}