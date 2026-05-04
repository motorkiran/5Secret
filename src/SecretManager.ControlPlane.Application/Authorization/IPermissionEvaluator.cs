namespace SecretManager.ControlPlane.Application.Authorization;

public interface IPermissionEvaluator
{
    Task<PermissionEvaluationResult> EvaluateAsync(
        PermissionEvaluationRequest request,
        CancellationToken cancellationToken);
}