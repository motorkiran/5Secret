namespace SecretManager.ControlPlane.Application.Bootstrap;

public sealed record BootstrapInstallationResult(
    Guid InstallationId,
    Guid OwnerUserId,
    string InstallationName,
    string OwnerUsername);