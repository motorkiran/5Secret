namespace SecretManager.ControlPlane.Application.Authorization;

public sealed record PermissionEvaluationRequest(
    Guid UserId,
    string Permission,
    IReadOnlyCollection<ResourceScope> ScopePath);