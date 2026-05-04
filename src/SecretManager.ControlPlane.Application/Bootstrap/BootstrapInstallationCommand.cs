namespace SecretManager.ControlPlane.Application.Bootstrap;

public sealed record BootstrapInstallationCommand(
    string InstallationName,
    string OwnerUsername,
    string OwnerDisplayName,
    string Password,
    string? CorrelationId = null,
    string? RemoteIpAddress = null);