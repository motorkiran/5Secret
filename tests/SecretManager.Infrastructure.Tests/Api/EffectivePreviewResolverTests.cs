using SecretManager.ControlPlane.Application.Catalog;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Installations;

namespace SecretManager.Infrastructure.Tests.Api;

public sealed class EffectivePreviewResolverTests
{
	[Fact]
	public void Resolve_PrefersHigherPrecedenceScopes_AndOrdersResults()
	{
		var applicationId = Guid.NewGuid();
		var environmentId = Guid.NewGuid();
		var nodeGroupId = Guid.NewGuid();
		var managedNodeId = Guid.NewGuid();
		var retriesConfigItemId = Guid.NewGuid();
		var apiUrlConfigItemId = Guid.NewGuid();
		var featureFlagConfigItemId = Guid.NewGuid();

		var resolvedItems = EffectivePreviewResolver.Resolve(
			new EffectivePreviewTarget(applicationId, environmentId, nodeGroupId, managedNodeId),
			[
				CreateCandidate(featureFlagConfigItemId, "FeatureFlag", ResourceScopeType.Environment, environmentId, "true", updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2)),
				CreateCandidate(retriesConfigItemId, "Trading:Core:MaxRetries", ResourceScopeType.Application, applicationId, "2", updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5)),
				CreateCandidate(retriesConfigItemId, "Trading:Core:MaxRetries", ResourceScopeType.Environment, environmentId, "3", updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-4)),
				CreateCandidate(retriesConfigItemId, "Trading:Core:MaxRetries", ResourceScopeType.NodeGroup, nodeGroupId, "4", updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-3)),
				CreateCandidate(retriesConfigItemId, "Trading:Core:MaxRetries", ResourceScopeType.ManagedNode, managedNodeId, "5", updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2)),
				CreateCandidate(retriesConfigItemId, "Trading:Core:MaxRetries", ResourceScopeType.EmergencyOverride, Installation.SingletonId, "9", updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1)),
				CreateCandidate(apiUrlConfigItemId, "Trading:Core:ApiUrl", ResourceScopeType.Application, applicationId, "\"https://app.example\"", updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2)),
				CreateCandidate(apiUrlConfigItemId, "Trading:Core:ApiUrl", ResourceScopeType.NodeGroup, nodeGroupId, "\"https://group.example\"", updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1))
			]);

		Assert.Collection(
			resolvedItems,
			featureFlag =>
			{
				Assert.Equal("FeatureFlag", featureFlag.FullPath);
				Assert.Equal("true", featureFlag.ValueJson);
				Assert.Equal(ResourceScopeType.Environment, featureFlag.SourceScopeType);
				Assert.Equal(environmentId, featureFlag.SourceScopeId);
			},
			apiUrl =>
			{
				Assert.Equal("Trading:Core:ApiUrl", apiUrl.FullPath);
				Assert.Equal("\"https://group.example\"", apiUrl.ValueJson);
				Assert.Equal(ResourceScopeType.NodeGroup, apiUrl.SourceScopeType);
				Assert.Equal(nodeGroupId, apiUrl.SourceScopeId);
			},
			retries =>
			{
				Assert.Equal("Trading:Core:MaxRetries", retries.FullPath);
				Assert.Equal("9", retries.ValueJson);
				Assert.Equal(ResourceScopeType.EmergencyOverride, retries.SourceScopeType);
				Assert.Equal(Installation.SingletonId, retries.SourceScopeId);
			});
	}

	[Fact]
	public void Resolve_IgnoresCandidatesFromOtherTargets()
	{
		var applicationId = Guid.NewGuid();
		var environmentId = Guid.NewGuid();
		var nodeGroupId = Guid.NewGuid();
		var managedNodeId = Guid.NewGuid();
		var configItemId = Guid.NewGuid();

		var resolvedItems = EffectivePreviewResolver.Resolve(
			new EffectivePreviewTarget(applicationId, environmentId, nodeGroupId, managedNodeId),
			[
				CreateCandidate(configItemId, "Trading:Core:TimeoutSeconds", ResourceScopeType.Application, applicationId, "30"),
				CreateCandidate(configItemId, "Trading:Core:TimeoutSeconds", ResourceScopeType.NodeGroup, Guid.NewGuid(), "45"),
				CreateCandidate(configItemId, "Trading:Core:TimeoutSeconds", ResourceScopeType.ManagedNode, Guid.NewGuid(), "60")
			]);

		var resolvedItem = Assert.Single(resolvedItems);
		Assert.Equal("30", resolvedItem.ValueJson);
		Assert.Equal(ResourceScopeType.Application, resolvedItem.SourceScopeType);
		Assert.Equal(applicationId, resolvedItem.SourceScopeId);
	}

	[Fact]
	public void Resolve_IncludesInstallationScopeAsBaseline_WhenNoMoreSpecificValueExists()
	{
		var applicationId = Guid.NewGuid();
		var environmentId = Guid.NewGuid();
		var nodeGroupId = Guid.NewGuid();
		var managedNodeId = Guid.NewGuid();
		var configItemId = Guid.NewGuid();

		var resolvedItems = EffectivePreviewResolver.Resolve(
			new EffectivePreviewTarget(applicationId, environmentId, nodeGroupId, managedNodeId),
			[
				CreateCandidate(configItemId, "Trading:Core:ApiKey", ResourceScopeType.Installation, Installation.SingletonId, "\"bootstrap-secret\"", isSecret: true),
				CreateCandidate(configItemId, "Trading:Core:ApiKey", ResourceScopeType.Application, Guid.NewGuid(), "\"other\"", isSecret: true)
			]);

		var resolvedItem = Assert.Single(resolvedItems);
		Assert.Equal("Trading:Core:ApiKey", resolvedItem.FullPath);
		Assert.Equal("\"bootstrap-secret\"", resolvedItem.ValueJson);
		Assert.Equal(ResourceScopeType.Installation, resolvedItem.SourceScopeType);
		Assert.Equal(Installation.SingletonId, resolvedItem.SourceScopeId);
	}

	private static EffectivePreviewCandidate CreateCandidate(
		Guid configItemId,
		string fullPath,
		ResourceScopeType scopeType,
		Guid scopeId,
		string valueJson,
		bool isSecret = false,
		string valueType = "string",
		DateTimeOffset? updatedAtUtc = null)
	{
		return new EffectivePreviewCandidate(
			DraftValueId: Guid.NewGuid(),
			ConfigItemId: configItemId,
			FullPath: fullPath,
			ValueType: valueType,
			ValueJson: valueJson,
			IsSecret: isSecret,
			ScopeType: scopeType,
			ScopeId: scopeId,
			UpdatedAtUtc: updatedAtUtc ?? DateTimeOffset.UtcNow);
	}
}