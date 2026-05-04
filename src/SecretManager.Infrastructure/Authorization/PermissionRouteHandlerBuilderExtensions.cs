using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SecretManager.ControlPlane.Application.Authorization;

namespace SecretManager.Infrastructure.Authorization;

public static class PermissionRouteHandlerBuilderExtensions
{
    public static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder builder,
        string permission,
        params ResourceScope[] scopePath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        if (scopePath.Length == 0)
        {
            throw new ArgumentException("At least one resource scope is required.", nameof(scopePath));
        }

        var capturedScopePath = scopePath.ToArray();

        builder.RequireAuthorization();
        builder.AddEndpointFilter(async (context, next) =>
        {
            var userIdClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var evaluator = context.HttpContext.RequestServices.GetRequiredService<IPermissionEvaluator>();
            var evaluation = await evaluator.EvaluateAsync(
                new PermissionEvaluationRequest(userId, permission, capturedScopePath),
                context.HttpContext.RequestAborted);

            return evaluation.IsAllowed
                ? await next(context)
                : Results.Forbid();
        });

        return builder;
    }
}