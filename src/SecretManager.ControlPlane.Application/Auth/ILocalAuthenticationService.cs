namespace SecretManager.ControlPlane.Application.Auth;

public interface ILocalAuthenticationService
{
    Task<AuthenticatedUserResult?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken);
}