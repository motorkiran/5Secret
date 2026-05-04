using SecretManager.Domain.Authorization;
using SecretManager.Domain.Installations;

namespace SecretManager.ControlPlane.Application.Catalog;

public sealed record EffectivePreviewTarget(
	Guid ApplicationId,
	Guid EnvironmentId,
	Guid? NodeGroupId,
	Guid? ManagedNodeId);

public sealed record EffectivePreviewCandidate(
	Guid DraftValueId,
	Guid ConfigItemId,
	string FullPath,
	string ValueType,
	string ValueJson,
	bool IsSecret,
	ResourceScopeType ScopeType,
	Guid ScopeId,
	DateTimeOffset UpdatedAtUtc);

public sealed record EffectivePreviewItem(
	Guid DraftValueId,
	Guid ConfigItemId,
	string FullPath,
	string ValueType,
	string ValueJson,
	bool IsSecret,
	ResourceScopeType SourceScopeType,
	Guid SourceScopeId,
	DateTimeOffset UpdatedAtUtc);

public static class EffectivePreviewResolver
{
	private static readonly IReadOnlyDictionary<ResourceScopeType, int> ScopePrecedence = new Dictionary<ResourceScopeType, int>
	{
		[ResourceScopeType.Installation] = 0,
		[ResourceScopeType.Application] = 1,
		[ResourceScopeType.Environment] = 2,
		[ResourceScopeType.NodeGroup] = 3,
		[ResourceScopeType.ManagedNode] = 4,
		[ResourceScopeType.EmergencyOverride] = 5
	};

	public static IReadOnlyList<EffectivePreviewItem> Resolve(
		EffectivePreviewTarget target,
		IEnumerable<EffectivePreviewCandidate> candidates)
	{
		ArgumentNullException.ThrowIfNull(candidates);

		if (target.ApplicationId == Guid.Empty)
		{
			throw new ArgumentException("ApplicationId is required.", nameof(target));
		}

		if (target.EnvironmentId == Guid.Empty)
		{
			throw new ArgumentException("EnvironmentId is required.", nameof(target));
		}

		if (target.NodeGroupId == Guid.Empty)
		{
			throw new ArgumentException("NodeGroupId cannot be an empty GUID when provided.", nameof(target));
		}

		if (target.ManagedNodeId == Guid.Empty)
		{
			throw new ArgumentException("ManagedNodeId cannot be an empty GUID when provided.", nameof(target));
		}

		var applicableScopes = GetApplicableScopes(target);
		var resolvedItems = new Dictionary<Guid, EffectivePreviewItem>();

		foreach (var candidate in candidates)
		{
			if (!applicableScopes.TryGetValue(candidate.ScopeType, out var applicableScopeId) || candidate.ScopeId != applicableScopeId)
			{
				continue;
			}

			var candidateItem = new EffectivePreviewItem(
				candidate.DraftValueId,
				candidate.ConfigItemId,
				candidate.FullPath,
				candidate.ValueType,
				candidate.ValueJson,
				candidate.IsSecret,
				candidate.ScopeType,
				candidate.ScopeId,
				candidate.UpdatedAtUtc);

			if (!resolvedItems.TryGetValue(candidate.ConfigItemId, out var existingItem) || ShouldReplace(existingItem, candidateItem))
			{
				resolvedItems[candidate.ConfigItemId] = candidateItem;
			}
		}

		return resolvedItems.Values
			.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
			.ThenBy(x => x.ConfigItemId)
			.ToArray();
	}

	private static Dictionary<ResourceScopeType, Guid> GetApplicableScopes(EffectivePreviewTarget target)
	{
		var scopes = new Dictionary<ResourceScopeType, Guid>
		{
			[ResourceScopeType.Installation] = Installation.SingletonId,
			[ResourceScopeType.Application] = target.ApplicationId,
			[ResourceScopeType.Environment] = target.EnvironmentId,
			[ResourceScopeType.EmergencyOverride] = Installation.SingletonId
		};

		if (target.NodeGroupId.HasValue)
		{
			scopes[ResourceScopeType.NodeGroup] = target.NodeGroupId.Value;
		}

		if (target.ManagedNodeId.HasValue)
		{
			scopes[ResourceScopeType.ManagedNode] = target.ManagedNodeId.Value;
		}

		return scopes;
	}

	private static bool ShouldReplace(EffectivePreviewItem existingItem, EffectivePreviewItem candidateItem)
	{
		var existingPrecedence = ScopePrecedence[existingItem.SourceScopeType];
		var candidatePrecedence = ScopePrecedence[candidateItem.SourceScopeType];
		if (candidatePrecedence != existingPrecedence)
		{
			return candidatePrecedence > existingPrecedence;
		}

		var updatedAtComparison = candidateItem.UpdatedAtUtc.CompareTo(existingItem.UpdatedAtUtc);
		if (updatedAtComparison != 0)
		{
			return updatedAtComparison > 0;
		}

		return candidateItem.DraftValueId.CompareTo(existingItem.DraftValueId) > 0;
	}
}