namespace SecretManager.ControlPlane.Application.Catalog;

public sealed record PublishedVersionPayloadDocument(
	Guid EnvironmentId,
	Guid ApplicationId,
	string RolloutPolicy,
	IReadOnlyList<PublishedVersionPayloadItem> DraftValues);

public sealed record PublishedVersionPayloadItem(
	Guid DraftValueId,
	Guid ConfigItemId,
	string FullPath,
	string ValueType,
	bool IsSecret,
	string ScopeType,
	Guid ScopeId,
	string ValueJson,
	string ChangeNote,
	DateTimeOffset UpdatedAtUtc);