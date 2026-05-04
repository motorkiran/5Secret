using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SecretManager.Agent.Worker.Configuration;
using SecretManager.Agent.Worker.ControlPlane;
using SecretManager.Agent.Worker.LocalState;
using SecretManager.Agent.Worker.Projection;
using SecretManager.Agent.Worker.Registration;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Agent.Worker.Runtime;

public interface IAgentCoordinator
{
	Task InitializeAsync(CancellationToken cancellationToken);

	Task SyncNowAsync(CancellationToken cancellationToken);

	Task ListenForInvalidationsAsync(CancellationToken cancellationToken);

	Task<bool> PromoteStagedSnapshotAsync(string applicationSlug, CancellationToken cancellationToken);

	AgentCoordinatorStatus GetStatus();

	IReadOnlyCollection<AgentApplicationRuntimeState> GetApplications();

	bool TryGetApplication(string applicationSlug, out AgentApplicationRuntimeState? state);
}

public sealed class AgentCoordinator(
	IOptions<AgentOptions> options,
	IAgentRegistrationStateStore registrationStateStore,
	IAgentLocalSnapshotStore localSnapshotStore,
	AgentRuntimeStateManager runtimeStateManager,
	IAgentProjectionService projectionService,
	IControlPlaneAgentClient controlPlaneAgentClient) : IAgentCoordinator
{
	private readonly Lock statusLock = new();
	private AgentRegistrationState? registrationState;
	private AgentCoordinatorStatus status = new("not-enrolled", false, true, null, null, null);

	public async Task InitializeAsync(CancellationToken cancellationToken)
	{
		registrationState = await registrationStateStore.LoadAsync(cancellationToken);
		if (registrationState is null)
		{
			if (options.Value.ManagedNodeId == Guid.Empty || string.IsNullOrWhiteSpace(options.Value.EnrollmentToken))
			{
				UpdateStatus(status with
				{
					HealthState = "not-enrolled",
					IsEnrolled = false,
					LastError = "Agent enrollment configuration is missing."
				});
				return;
			}

			var enrollment = await controlPlaneAgentClient.EnrollAsync(
				new AgentEnrollRequest
				{
					ManagedNodeId = options.Value.ManagedNodeId,
					Hostname = options.Value.Hostname,
					Platform = options.Value.Platform,
					AgentVersion = options.Value.AgentVersion,
					EnrollmentToken = options.Value.EnrollmentToken
				},
				cancellationToken);

			registrationState = new AgentRegistrationState
			{
				AgentId = enrollment.AgentId,
				ManagedNodeId = enrollment.ManagedNodeId,
				EnvironmentId = enrollment.EnvironmentId,
				AgentCredential = enrollment.AgentCredential,
				EnrollmentSecret = enrollment.EnrollmentSecret,
				LocalSalt = GenerateLocalSalt(),
				EnrolledAtUtc = enrollment.EnrolledAtUtc
			};

			await registrationStateStore.SaveAsync(registrationState, cancellationToken);
		}

		var loadResult = await localSnapshotStore.LoadAsync(
			registrationState.EnrollmentSecret,
			registrationState.LocalSalt,
			cancellationToken);
		if (loadResult.Success && loadResult.State is not null)
		{
			runtimeStateManager.Restore(loadResult.State, DateTimeOffset.UtcNow);
			UpdateStatus(status with
			{
				HealthState = "degraded-offline",
				IsEnrolled = true,
				LocalStoreHealthy = true,
				RecoveredAtUtc = DateTimeOffset.UtcNow,
				LastError = null
			});
		}
		else if (!loadResult.Success)
		{
			UpdateStatus(status with
			{
				HealthState = "sync-failed",
				IsEnrolled = true,
				LocalStoreHealthy = false,
				LastError = loadResult.FailureReason
			});
		}

		await SyncNowAsync(cancellationToken);
	}

	public async Task SyncNowAsync(CancellationToken cancellationToken)
	{
		if (registrationState is null)
		{
			UpdateStatus(status with
			{
				HealthState = "not-enrolled",
				IsEnrolled = false,
				LastError = "Agent registration has not been initialized."
			});
			return;
		}

		try
		{
			var syncCheck = await controlPlaneAgentClient.SyncCheckAsync(
				registrationState.AgentId,
				registrationState.AgentCredential,
				cancellationToken);

			foreach (var snapshotReference in syncCheck.Snapshots.OrderBy(x => x.ApplicationSlug, StringComparer.OrdinalIgnoreCase))
			{
				if (TryGetApplication(snapshotReference.ApplicationSlug, out var existing)
					&& ((existing!.ActiveSnapshot?.PublishedVersionId == snapshotReference.PublishedVersionId
						&& existing.ActiveSnapshot.SnapshotHash == snapshotReference.SnapshotHash)
						|| (existing.StagedSnapshot?.PublishedVersionId == snapshotReference.PublishedVersionId
							&& existing.StagedSnapshot.SnapshotHash == snapshotReference.SnapshotHash)))
				{
					continue;
				}

				var snapshot = await controlPlaneAgentClient.FetchSnapshotAsync(
					snapshotReference.SnapshotId,
					registrationState.AgentId,
					registrationState.AgentCredential,
					cancellationToken);
				if (!AgentSnapshotIntegrity.Validate(snapshot)
					|| !string.Equals(snapshot.SnapshotHash, snapshotReference.SnapshotHash, StringComparison.Ordinal))
				{
					throw new InvalidOperationException($"Snapshot '{snapshotReference.SnapshotId}' failed integrity validation.");
				}

				var activation = runtimeStateManager.ApplySnapshot(snapshotReference, snapshot, DateTimeOffset.UtcNow);
				if (activation.Mode == AgentSnapshotActivationMode.Active)
				{
					await projectionService.ProjectAsync(activation.State, cancellationToken);
				}
				else
				{
					projectionService.MarkPending(snapshotReference.ApplicationSlug);
				}
			}

			await PersistLocalStateAsync(cancellationToken);
			await controlPlaneAgentClient.SendHeartbeatAsync(BuildHeartbeatRequest(), cancellationToken);

			UpdateStatus(status with
			{
				HealthState = "healthy",
				IsEnrolled = true,
				LocalStoreHealthy = true,
				LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow,
				LastError = null
			});
		}
		catch (Exception ex)
		{
			UpdateStatus(status with
			{
				HealthState = runtimeStateManager.GetApplications().Count > 0 ? "degraded-offline" : "sync-failed",
				IsEnrolled = true,
				LastError = ex.Message
			});
		}
	}

	public async Task ListenForInvalidationsAsync(CancellationToken cancellationToken)
	{
		if (registrationState is null)
		{
			return;
		}

		await foreach (var _ in controlPlaneAgentClient.SubscribeInvalidationsAsync(
			registrationState.AgentId,
			registrationState.AgentCredential,
			cancellationToken))
		{
			await SyncNowAsync(cancellationToken);
		}
	}

	public async Task<bool> PromoteStagedSnapshotAsync(string applicationSlug, CancellationToken cancellationToken)
	{
		var promoted = runtimeStateManager.TryPromoteStagedSnapshot(applicationSlug, DateTimeOffset.UtcNow, out var state);
		if (!promoted)
		{
			return false;
		}

		await PersistLocalStateAsync(cancellationToken);
		await projectionService.ProjectAsync(state!, cancellationToken);
		return true;
	}

	public AgentCoordinatorStatus GetStatus()
	{
		lock (statusLock)
		{
			return status;
		}
	}

	public IReadOnlyCollection<AgentApplicationRuntimeState> GetApplications()
	{
		return runtimeStateManager.GetApplications();
	}

	public bool TryGetApplication(string applicationSlug, out AgentApplicationRuntimeState? state)
	{
		return runtimeStateManager.TryGetApplication(applicationSlug, out state);
	}

	private async Task PersistLocalStateAsync(CancellationToken cancellationToken)
	{
		if (registrationState is null)
		{
			return;
		}

		await localSnapshotStore.SaveAsync(
			runtimeStateManager.Export(DateTimeOffset.UtcNow),
			registrationState.EnrollmentSecret,
			registrationState.LocalSalt,
			cancellationToken);
	}

	private AgentHeartbeatRequest BuildHeartbeatRequest()
	{
		if (registrationState is null)
		{
			throw new InvalidOperationException("Agent registration state is not available.");
		}

		var currentSnapshot = runtimeStateManager.GetApplications()
			.Select(x => x.ActiveSnapshot)
			.Where(x => x is not null)
			.OrderByDescending(x => x!.UpdatedAtUtc)
			.FirstOrDefault();

		return new AgentHeartbeatRequest
		{
			AgentId = registrationState.AgentId,
			AgentCredential = registrationState.AgentCredential,
			AgentVersion = options.Value.AgentVersion,
			CurrentPublishedVersionId = currentSnapshot?.PublishedVersionId,
			CurrentVersionNumber = currentSnapshot?.VersionNumber
		};
	}

	private static string GenerateLocalSalt()
	{
		return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
	}

	private void UpdateStatus(AgentCoordinatorStatus nextStatus)
	{
		lock (statusLock)
		{
			status = nextStatus;
		}
	}
}

public sealed record AgentCoordinatorStatus(
	string HealthState,
	bool IsEnrolled,
	bool LocalStoreHealthy,
	DateTimeOffset? LastSuccessfulSyncAtUtc,
	string? LastError,
	DateTimeOffset? RecoveredAtUtc);