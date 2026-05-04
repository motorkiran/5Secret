using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Net.Http.Json;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Agent.Worker.ControlPlane;

public sealed class HttpControlPlaneAgentClient(HttpClient httpClient) : IControlPlaneAgentClient
{
	public async Task<AgentEnrollmentResponse> EnrollAsync(AgentEnrollRequest request, CancellationToken cancellationToken)
	{
		var response = await httpClient.PostAsJsonAsync("/api/v1/agent/enroll", request, cancellationToken);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<AgentEnrollmentResponse>(cancellationToken: cancellationToken)
			?? throw new InvalidOperationException("Control plane enrollment returned an empty response.");
	}

	public async Task<AgentSnapshotResponse> FetchSnapshotAsync(string snapshotId, Guid agentId, string agentCredential, CancellationToken cancellationToken)
	{
		var response = await httpClient.GetAsync(
			$"/api/v1/agent/snapshots/{snapshotId}?agentId={agentId}&agentCredential={Uri.EscapeDataString(agentCredential)}",
			cancellationToken);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<AgentSnapshotResponse>(cancellationToken: cancellationToken)
			?? throw new InvalidOperationException("Control plane snapshot fetch returned an empty response.");
	}

	public async Task SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken)
	{
		var response = await httpClient.PostAsJsonAsync("/api/v1/agent/heartbeat", request, cancellationToken);
		response.EnsureSuccessStatusCode();
	}

	public async IAsyncEnumerable<AgentInvalidationNotification> SubscribeInvalidationsAsync(
		Guid agentId,
		string agentCredential,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var response = await httpClient.GetAsync(
			$"/api/v1/agent/notifications/stream?agentId={agentId}&agentCredential={Uri.EscapeDataString(agentCredential)}",
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var reader = new StreamReader(stream);
		while (!cancellationToken.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line is null)
			{
				break;
			}

			if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
			{
				continue;
			}

			var notification = JsonSerializer.Deserialize<AgentInvalidationNotification>(line[6..]);
			if (notification is not null)
			{
				yield return notification;
			}
		}
	}

	public async Task<AgentSyncCheckResponse> SyncCheckAsync(Guid agentId, string agentCredential, CancellationToken cancellationToken)
	{
		var response = await httpClient.PostAsJsonAsync(
			"/api/v1/agent/sync/check",
			new AgentSyncCheckRequest
			{
				AgentId = agentId,
				AgentCredential = agentCredential
			},
			cancellationToken);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<AgentSyncCheckResponse>(cancellationToken: cancellationToken)
			?? throw new InvalidOperationException("Control plane sync-check returned an empty response.");
	}
}