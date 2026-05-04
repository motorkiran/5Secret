using System.ComponentModel.DataAnnotations;

namespace SecretManager.Shared.Contracts.Bootstrap;

public sealed class BootstrapInstallationRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string InstallationName { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(64)]
    public string OwnerUsername { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string OwnerDisplayName { get; set; } = string.Empty;

    [Required]
    [MinLength(12)]
    [MaxLength(200)]
    public string Password { get; set; } = string.Empty;
}

public sealed record BootstrapInstallationResponse(
    Guid InstallationId,
    Guid OwnerUserId,
    string InstallationName,
    string OwnerUsername);

public sealed record BootstrapStatusResponse(bool IsInitialized, string? InstallationName);