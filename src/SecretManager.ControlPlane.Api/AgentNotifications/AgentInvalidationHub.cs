using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.ControlPlane.Api.AgentNotifications;

public interface IAgentInvalidationHub
{
	IAsyncEnumerable<AgentInvalidationNotification> SubscribeAsync(Guid agentId, CancellationToken cancellationToken);

	Task PublishAsync(IEnumerable<Guid> agentIds, AgentInvalidationNotification notification, CancellationToken cancellationToken);
}

public sealed class AgentInvalidationHub : IAgentInvalidationHub
{
	private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<AgentInvalidationNotification>>> subscriptions = new();

	public async Task PublishAsync(IEnumerable<Guid> agentIds, AgentInvalidationNotification notification, CancellationToken cancellationToken)
	{
		foreach (var agentId in agentIds.Distinct())
		{
			if (!subscriptions.TryGetValue(agentId, out var channels))
			{
				continue;
			}

			foreach (var channel in channels.Values)
			{
				await channel.Writer.WriteAsync(notification, cancellationToken);
			}
		}
	}

	public async IAsyncEnumerable<AgentInvalidationNotification> SubscribeAsync(
		Guid agentId,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var subscriptionId = Guid.NewGuid();
		var channel = Channel.CreateUnbounded<AgentInvalidationNotification>();
		var channels = subscriptions.GetOrAdd(agentId, _ => new ConcurrentDictionary<Guid, Channel<AgentInvalidationNotification>>());
		channels[subscriptionId] = channel;

		try
		{
			while (await channel.Reader.WaitToReadAsync(cancellationToken))
			{
				while (channel.Reader.TryRead(out var notification))
				{
					yield return notification;
				}
			}
		}
		finally
		{
			channel.Writer.TryComplete();
			if (subscriptions.TryGetValue(agentId, out var existingChannels))
			{
				existingChannels.TryRemove(subscriptionId, out _);
				if (existingChannels.IsEmpty)
				{
					subscriptions.TryRemove(agentId, out _);
				}
			}
		}
	}
}