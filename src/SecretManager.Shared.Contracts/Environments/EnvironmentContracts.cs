using System.ComponentModel.DataAnnotations;

namespace SecretManager.Shared.Contracts.Environments;

public sealed class CreateEnvironmentRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Slug { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public bool IsProtected { get; set; }
}

public sealed class UpdateEnvironmentRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Slug { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public bool IsProtected { get; set; }
}

public sealed record EnvironmentSummaryResponse(
    Guid EnvironmentId,
    string Name,
    string Slug,
    string Description,
    bool IsProtected,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);