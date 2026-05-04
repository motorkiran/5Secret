namespace SecretManager.ControlPlane.Application.Bootstrap;

public interface IBootstrapService
{
    Task<BootstrapStatusResult> GetStatusAsync(CancellationToken cancellationToken);

    Task<BootstrapInstallationResult> BootstrapAsync(
        BootstrapInstallationCommand command,
        CancellationToken cancellationToken);
}