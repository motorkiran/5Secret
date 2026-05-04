using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Agent.Worker.ControlPlane;

public interface IControlPlaneAgentClient
{
	Task<AgentEnrollmentResponse> EnrollAsync(AgentEnrollRequest request, CancellationToken cancellationToken);

	Task<AgentSyncCheckResponse> SyncCheckAsync(Guid agentId, string agentCredential, CancellationToken cancellationToken);

	Task<AgentSnapshotResponse> FetchSnapshotAsync(string snapshotId, Guid agentId, string agentCredential, CancellationToken cancellationToken);

	Task SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken);

	IAsyncEnumerable<AgentInvalidationNotification> SubscribeInvalidationsAsync(Guid agentId, string agentCredential, CancellationToken cancellationToken);
}