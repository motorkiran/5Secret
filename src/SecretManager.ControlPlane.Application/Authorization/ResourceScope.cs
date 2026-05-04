using SecretManager.Domain.Authorization;

namespace SecretManager.ControlPlane.Application.Authorization;

public sealed record ResourceScope(ResourceScopeType ScopeType, Guid ScopeId);