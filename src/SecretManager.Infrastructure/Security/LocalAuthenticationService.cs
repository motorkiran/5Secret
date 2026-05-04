using Microsoft.EntityFrameworkCore;
using SecretManager.ControlPlane.Application.Auth;
using SecretManager.Infrastructure.Persistence;

namespace SecretManager.Infrastructure.Security;

internal sealed class LocalAuthenticationService(
    SecretManagerDbContext dbContext,
    Argon2PasswordHasher passwordHasher) : ILocalAuthenticationService
{
    public async Task<AuthenticatedUserResult?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username.Trim();

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == normalizedUsername, cancellationToken);

        if (user is null || !user.IsEnabled)
        {
            return null;
        }

        if (!passwordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return new AuthenticatedUserResult(user.Id, user.Username, user.DisplayName, user.Role);
    }
}