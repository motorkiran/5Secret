using SecretManager.Domain.Authorization;

namespace SecretManager.ControlPlane.Application.Authorization;

public sealed record PermissionEvaluationResult(
    bool IsAllowed,
    string? MatchedRoleName,
    ResourceScopeType? GrantedScopeType,
    Guid? GrantedScopeId);