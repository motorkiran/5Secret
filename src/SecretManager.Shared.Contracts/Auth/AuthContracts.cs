using System.ComponentModel.DataAnnotations;

namespace SecretManager.Shared.Contracts.Auth;

public sealed class LoginRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(12)]
    [MaxLength(200)]
    public string Password { get; set; } = string.Empty;
}

public sealed record LoginResponse(Guid UserId, string Username, string DisplayName, string Role);

public sealed record CurrentUserResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string Role,
    bool IsAuthenticated);