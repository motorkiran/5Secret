using System.ComponentModel.DataAnnotations;

namespace SecretManager.Shared.Contracts.Authorization;

public sealed record UserSummaryResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    bool IsEnabled,
    string Role);

public sealed record RoleSummaryResponse(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystem,
    string[] Permissions);

public sealed class CreateRoleAssignmentRequest
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(64)]
    public string ScopeType { get; set; } = string.Empty;

    public Guid ScopeId { get; set; }
}

public sealed record CreateRoleAssignmentResponse(
    Guid AssignmentId,
    Guid UserId,
    Guid RoleId,
    string ScopeType,
    Guid ScopeId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);