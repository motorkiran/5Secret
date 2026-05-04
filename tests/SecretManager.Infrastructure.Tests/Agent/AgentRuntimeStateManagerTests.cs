using SecretManager.Agent.Worker.Runtime;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Infrastructure.Tests.Agent;

public sealed class AgentRuntimeStateManagerTests
{
	[Fact]
	public void ApplySnapshot_ActivatesImmediateSnapshot_AndStagesManualReloadUpdate()
	{
		var manager = new AgentRuntimeStateManager();
		var firstReference = CreateSnapshotReference(versionNumber: 1, rolloutPolicy: "immediate");
		var firstSnapshot = CreateSnapshot(versionNumber: 1, rolloutPolicy: "immediate");
		var initial = manager.ApplySnapshot(firstReference, firstSnapshot, DateTimeOffset.Parse("2026-05-03T21:00:00Z"));

		Assert.Equal(AgentSnapshotActivationMode.Active, initial.Mode);
		Assert.Equal("active", initial.State.ActivationState);
		Assert.Equal(firstSnapshot.PublishedVersionId, initial.State.ActiveSnapshot!.PublishedVersionId);

		var stagedReference = CreateSnapshotReference(versionNumber: 2, rolloutPolicy: "manual-reload");
		var stagedSnapshot = CreateSnapshot(versionNumber: 2, rolloutPolicy: "manual-reload");
		var staged = manager.ApplySnapshot(stagedReference, stagedSnapshot, DateTimeOffset.Parse("2026-05-03T21:05:00Z"));

		Assert.Equal(AgentSnapshotActivationMode.Staged, staged.Mode);
		Assert.Equal("staged", staged.State.ActivationState);
		Assert.Equal(firstSnapshot.PublishedVersionId, staged.State.ActiveSnapshot!.PublishedVersionId);
		Assert.Equal(stagedSnapshot.PublishedVersionId, staged.State.StagedSnapshot!.PublishedVersionId);
	}

	[Fact]
	public void TryPromoteStagedSnapshot_ActivatesTheQueuedSnapshot_AndExportRoundTrips()
	{
		var manager = new AgentRuntimeStateManager();
		manager.ApplySnapshot(
			CreateSnapshotReference(versionNumber: 1, rolloutPolicy: "immediate"),
			CreateSnapshot(versionNumber: 1, rolloutPolicy: "immediate"),
			DateTimeOffset.Parse("2026-05-03T21:00:00Z"));
		manager.ApplySnapshot(
			CreateSnapshotReference(versionNumber: 2, rolloutPolicy: "restart-required"),
			CreateSnapshot(versionNumber: 2, rolloutPolicy: "restart-required"),
			DateTimeOffset.Parse("2026-05-03T21:05:00Z"));

		var promoted = manager.TryPromoteStagedSnapshot("trading-api", DateTimeOffset.Parse("2026-05-03T21:06:00Z"), out var promotedState);

		Assert.True(promoted);
		Assert.NotNull(promotedState);
		Assert.Equal("active", promotedState!.ActivationState);
		Assert.Equal(2, promotedState.ActiveSnapshot!.VersionNumber);
		Assert.Null(promotedState.StagedSnapshot);

		var exported = manager.Export(DateTimeOffset.Parse("2026-05-03T21:07:00Z"));
		var restored = new AgentRuntimeStateManager();
		restored.Restore(exported, DateTimeOffset.Parse("2026-05-03T21:08:00Z"));

		Assert.True(restored.TryGetApplication("trading-api", out var restoredState));
		Assert.NotNull(restoredState);
		Assert.Equal(2, restoredState!.ActiveSnapshot!.VersionNumber);
		Assert.Equal("active", restoredState.ActivationState);
	}

	private static AgentSyncSnapshotReferenceResponse CreateSnapshotReference(int versionNumber, string rolloutPolicy)
	{
		var publishedVersionId = Guid.Parse($"00000000-0000-0000-0000-{versionNumber:000000000000}");
		var snapshotId = $"{publishedVersionId:N}.{Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"):N}.{Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"):N}";
		return new AgentSyncSnapshotReferenceResponse(
			snapshotId,
			Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
			"trading-api",
			publishedVersionId,
			versionNumber,
			$"sha256:ref-{versionNumber}",
			rolloutPolicy);
	}

	private static AgentSnapshotResponse CreateSnapshot(int versionNumber, string rolloutPolicy)
	{
		var managedNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		var applicationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		var publishedVersionId = Guid.Parse($"00000000-0000-0000-0000-{versionNumber:000000000000}");
		var updatedAtUtc = DateTimeOffset.Parse("2026-05-03T21:00:00Z").AddMinutes(versionNumber);
		var snapshotId = $"{publishedVersionId:N}.{managedNodeId:N}.{applicationId:N}";
		var values = new List<AgentSnapshotValueResponse>
		{
			new(
				Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
				"Trading:Core:MaxRetries",
				"integer",
				versionNumber.ToString(),
				false,
				"Application",
				applicationId)
		};
		var snapshotHash = AgentSnapshotIntegrity.ComputeHash(
			snapshotId,
			managedNodeId,
			applicationId,
			publishedVersionId,
			versionNumber,
			rolloutPolicy,
			updatedAtUtc,
			values);

		return new AgentSnapshotResponse(
			snapshotId,
			managedNodeId,
			applicationId,
			publishedVersionId,
			versionNumber,
			snapshotHash,
			rolloutPolicy,
			updatedAtUtc,
			"canonical",
			values);
	}
}