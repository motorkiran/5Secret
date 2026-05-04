using System.Collections.Concurrent;
using SecretManager.Agent.Worker.LocalState;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Agent.Worker.Runtime;

public sealed class AgentRuntimeStateManager
{
	private readonly ConcurrentDictionary<string, AgentApplicationRuntimeState> applications = new(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyCollection<AgentApplicationRuntimeState> GetApplications()
	{
		return applications.Values
			.OrderBy(x => x.ApplicationSlug, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public bool TryGetApplication(string applicationSlug, out AgentApplicationRuntimeState? state)
	{
		return applications.TryGetValue(applicationSlug, out state);
	}

	public AgentSnapshotActivationResult ApplySnapshot(
		AgentSyncSnapshotReferenceResponse snapshotReference,
		AgentSnapshotResponse snapshot,
		DateTimeOffset observedAtUtc)
	{
		ArgumentNullException.ThrowIfNull(snapshotReference);
		ArgumentNullException.ThrowIfNull(snapshot);

		var state = applications.AddOrUpdate(
			snapshotReference.ApplicationSlug,
			_ => CreateInitialState(snapshotReference),
			(_, existing) => existing with { ApplicationId = snapshotReference.ApplicationId, ApplicationSlug = snapshotReference.ApplicationSlug });

		var activationMode = ShouldActivateImmediately(state, snapshotReference.RolloutPolicy)
			? AgentSnapshotActivationMode.Active
			: AgentSnapshotActivationMode.Staged;

		var updatedState = activationMode == AgentSnapshotActivationMode.Active
			? state with
			{
				ActiveSnapshot = snapshot,
				StagedSnapshot = null,
				ActivationState = "active",
				LastObservedAtUtc = observedAtUtc
			}
			: state with
			{
				StagedSnapshot = snapshot,
				ActivationState = snapshotReference.RolloutPolicy.Equals("restart-required", StringComparison.OrdinalIgnoreCase)
					? "restart-required"
					: "staged",
				LastObservedAtUtc = observedAtUtc
			};

		applications[snapshotReference.ApplicationSlug] = updatedState;
		return new AgentSnapshotActivationResult(updatedState, activationMode);
	}

	public bool TryPromoteStagedSnapshot(string applicationSlug, DateTimeOffset promotedAtUtc, out AgentApplicationRuntimeState? state)
	{
		state = null;
		if (!applications.TryGetValue(applicationSlug, out var existing) || existing.StagedSnapshot is null)
		{
			return false;
		}

		state = existing with
		{
			ActiveSnapshot = existing.StagedSnapshot,
			StagedSnapshot = null,
			ActivationState = "active",
			LastObservedAtUtc = promotedAtUtc
		};
		applications[applicationSlug] = state;
		return true;
	}

	public void Restore(AgentPersistedRuntimeStateDocument document, DateTimeOffset restoredAtUtc)
	{
		ArgumentNullException.ThrowIfNull(document);
		applications.Clear();

		foreach (var application in document.Applications)
		{
			applications[application.ApplicationSlug] = new AgentApplicationRuntimeState(
				application.ApplicationId,
				application.ApplicationSlug,
				application.ActiveSnapshot,
				application.StagedSnapshot,
				application.StagedSnapshot is null ? "active" : "staged",
				restoredAtUtc);
		}
	}

	public AgentPersistedRuntimeStateDocument Export(DateTimeOffset persistedAtUtc)
	{
		return new AgentPersistedRuntimeStateDocument
		{
			PersistedAtUtc = persistedAtUtc,
			Applications = applications.Values
				.OrderBy(x => x.ApplicationSlug, StringComparer.OrdinalIgnoreCase)
				.Select(x => new AgentPersistedApplicationState
				{
					ApplicationId = x.ApplicationId,
					ApplicationSlug = x.ApplicationSlug,
					ActiveSnapshot = x.ActiveSnapshot,
					StagedSnapshot = x.StagedSnapshot
				})
				.ToList()
		};
	}

	private static AgentApplicationRuntimeState CreateInitialState(AgentSyncSnapshotReferenceResponse snapshotReference)
	{
		return new AgentApplicationRuntimeState(
			snapshotReference.ApplicationId,
			snapshotReference.ApplicationSlug,
			null,
			null,
			"empty",
			null);
	}

	private static bool ShouldActivateImmediately(AgentApplicationRuntimeState state, string rolloutPolicy)
	{
		if (state.ActiveSnapshot is null)
		{
			return true;
		}

		return rolloutPolicy.Equals("immediate", StringComparison.OrdinalIgnoreCase)
			|| rolloutPolicy.Equals("file-only", StringComparison.OrdinalIgnoreCase);
	}
}

public sealed record AgentApplicationRuntimeState(
	Guid ApplicationId,
	string ApplicationSlug,
	AgentSnapshotResponse? ActiveSnapshot,
	AgentSnapshotResponse? StagedSnapshot,
	string ActivationState,
	DateTimeOffset? LastObservedAtUtc);

public sealed record AgentSnapshotActivationResult(
	AgentApplicationRuntimeState State,
	AgentSnapshotActivationMode Mode);

public enum AgentSnapshotActivationMode
{
	Active,
	Staged
}