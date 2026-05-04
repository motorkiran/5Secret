namespace SecretManager.ControlPlane.Application.Bootstrap;

public sealed record BootstrapStatusResult(bool IsInitialized, string? InstallationName);