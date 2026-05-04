namespace SecretManager.ControlPlane.Application.Auth;

public sealed record AuthenticatedUserResult(
    Guid UserId,
    string Username,
    string DisplayName,
    string Role);