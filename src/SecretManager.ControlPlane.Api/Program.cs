using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.EntityFrameworkCore;
using SecretManager.ControlPlane.Application.Auth;
using SecretManager.ControlPlane.Application.Authorization;
using SecretManager.ControlPlane.Application.Catalog;
using SecretManager.Domain.Agents;
using SecretManager.Domain.Authorization;
using SecretManager.Domain.Catalog;
using SecretManager.Domain.Environments;
using SecretManager.Domain.Installations;
using SecretManager.Domain.Topology;
using SecretManager.Infrastructure.Distribution;
using SecretManager.Infrastructure.Hosting;
using SecretManager.Infrastructure.Persistence;
using SecretManager.Infrastructure.Authorization;
using SecretManager.Infrastructure.Security;
using SecretManager.ControlPlane.Application.Bootstrap;
using SecretManager.Shared.Contracts.Auth;
using SecretManager.Shared.Contracts.Agents;
using SecretManager.Shared.Contracts.Audit;
using SecretManager.Shared.Contracts.Authorization;
using SecretManager.Shared.Contracts.Bootstrap;
using SecretManager.Shared.Contracts.Catalog;
using SecretManager.Shared.Contracts.Configuration;
using SecretManager.Shared.Contracts.Environments;
using SecretManager.Shared.Contracts.Topology;
using SecretManager.Infrastructure.Auditing;
using SecretManager.ControlPlane.Api.AgentNotifications;

var builder = WebApplication.CreateBuilder(args);
builder.AddSecretManagerServiceDefaults("secretmanager-control-plane-api", isWeb: true);
builder.Services.AddSecretManagerPersistence(builder.Configuration);
builder.Services.AddSecretManagerLocalAuthentication();
builder.Services.AddStackExchangeRedisCache(options =>
{
	options.Configuration = builder.Configuration[$"{RedisOptions.SectionName}:Configuration"];
	options.InstanceName = builder.Configuration[$"{RedisOptions.SectionName}:InstanceName"];
});
builder.Services.AddSingleton<IAgentSnapshotCache, RedisAgentSnapshotCache>();
builder.Services.AddSingleton<IAgentInvalidationHub, AgentInvalidationHub>();

var app = builder.Build();

await app.InitializeSecretManagerPersistenceAsync();

app.UseSecretManagerCorrelationId();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (HttpContext context, IHostEnvironment environment) => Results.Ok(new
{
	service = "secretmanager-control-plane-api",
	environment = environment.EnvironmentName,
	correlationId = context.TraceIdentifier,
	status = "running"
}));

app.MapPost(
	"/api/v1/auth/login",
	async (
		LoginRequest request,
		ILocalAuthenticationService authenticationService,
		IAuditEventWriter auditEventWriter,
		HttpContext httpContext,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request);
		if (validationErrors is not null)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var normalizedUsername = request.Username.Trim();

		var authenticatedUser = await authenticationService.AuthenticateAsync(
			normalizedUsername,
			request.Password,
			cancellationToken);

		if (authenticatedUser is null)
		{
			await auditEventWriter.WriteAsync(
				CreateAuditRequest(
					httpContext,
					action: "auth.login",
					targetType: "UserAccount",
					targetIdentifier: normalizedUsername,
					targetDisplayName: normalizedUsername,
					outcome: "Failed",
					details: new Dictionary<string, object?>
					{
						["reason"] = "invalid_credentials"
					}),
				cancellationToken);

			return Results.Unauthorized();
		}

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, authenticatedUser.UserId.ToString()),
			new(ClaimTypes.Name, authenticatedUser.Username),
			new(ClaimTypes.GivenName, authenticatedUser.DisplayName),
			new(ClaimTypes.Role, authenticatedUser.Role)
		};

		var principal = new ClaimsPrincipal(
			new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

		await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

		await auditEventWriter.WriteAsync(
			CreateAuditRequest(
				httpContext,
				action: "auth.login",
				targetType: "UserAccount",
				targetIdentifier: authenticatedUser.UserId.ToString(),
				targetDisplayName: authenticatedUser.Username,
				outcome: "Succeeded",
				actorUserId: authenticatedUser.UserId,
				actorUsername: authenticatedUser.Username),
			cancellationToken);

		return Results.Ok(new LoginResponse(
			authenticatedUser.UserId,
			authenticatedUser.Username,
			authenticatedUser.DisplayName,
			authenticatedUser.Role));
	});

app.MapPost(
	"/api/v1/auth/logout",
	async (
		HttpContext httpContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var (actorUserId, actorUsername) = GetAuditActor(httpContext.User);
		await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

		if (actorUserId is not null || !string.IsNullOrWhiteSpace(actorUsername))
		{
			await auditEventWriter.WriteAsync(
				CreateAuditRequest(
					httpContext,
					action: "auth.logout",
					targetType: "UserAccount",
					targetIdentifier: actorUserId?.ToString() ?? actorUsername!,
					targetDisplayName: actorUsername,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername),
				cancellationToken);
		}

		return Results.NoContent();
	});

app.MapGet(
	"/api/v1/auth/me",
	(ClaimsPrincipal user) =>
	{
		var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
			? parsedUserId
			: Guid.Empty;

		return Results.Ok(new CurrentUserResponse(
			userId,
			user.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
			user.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty,
			user.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
			user.Identity?.IsAuthenticated ?? false));
	})
	.RequireAuthorization();

app.MapGet(
	"/api/v1/users",
	async (SecretManagerDbContext dbContext, CancellationToken cancellationToken) =>
	{
		var users = await dbContext.Users
			.AsNoTracking()
			.OrderBy(x => x.Username)
			.Select(x => new UserSummaryResponse(
				x.Id,
				x.Username,
				x.DisplayName,
				x.IsEnabled,
				x.Role))
			.ToListAsync(cancellationToken);

		return Results.Ok(users);
	})
	.RequirePermission(PermissionCatalog.UsersRead, InstallationScopePath());

app.MapGet(
	"/api/v1/roles",
	async (SecretManagerDbContext dbContext, CancellationToken cancellationToken) =>
	{
		var roles = await dbContext.RoleDefinitions
			.AsNoTracking()
			.OrderBy(x => x.Name)
			.Select(x => new RoleSummaryResponse(
				x.Id,
				x.Name,
				x.Description,
				x.IsSystem,
				x.Permissions
					.OrderBy(permission => permission.Permission)
					.Select(permission => permission.Permission)
					.ToArray()))
			.ToListAsync(cancellationToken);

		return Results.Ok(roles);
	})
	.RequirePermission(PermissionCatalog.RolesRead, InstallationScopePath());

app.MapGet(
	"/api/v1/environments",
	async (SecretManagerDbContext dbContext, CancellationToken cancellationToken) =>
	{
		var environments = await dbContext.Environments
			.AsNoTracking()
			.OrderBy(x => x.Name)
			.Select(x => new EnvironmentSummaryResponse(
				x.Id,
				x.Name,
				x.Slug,
				x.Description,
				x.IsProtected,
				x.CreatedAtUtc,
				x.UpdatedAtUtc))
			.ToListAsync(cancellationToken);

		return Results.Ok(environments);
	})
	.RequirePermission(PermissionCatalog.EnvironmentsRead, InstallationScopePath());

app.MapPost(
	"/api/v1/environments",
	async (
		CreateEnvironmentRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedSlug = NormalizeSlug(request.Slug, normalizedName);
		if (normalizedSlug.Length == 0)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must contain at least one letter or number."];
		}
		else if (normalizedSlug.Length > 128)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must be 128 characters or fewer."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		if (await dbContext.Environments.AnyAsync(x => x.Name.ToUpper() == normalizedNameUpper, cancellationToken))
		{
			return Results.Problem(
				title: "Environment name conflict",
				detail: $"Environment '{normalizedName}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.Environments.AnyAsync(x => x.Slug == normalizedSlug, cancellationToken))
		{
			return Results.Problem(
				title: "Environment slug conflict",
				detail: $"Environment slug '{normalizedSlug}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var now = DateTimeOffset.UtcNow;
		var environment = new EnvironmentDefinition
		{
			Id = Guid.NewGuid(),
			Name = normalizedName,
			Slug = normalizedSlug,
			Description = NormalizeOptionalText(request.Description),
			IsProtected = request.IsProtected,
			CreatedAtUtc = now,
			UpdatedAtUtc = now
		};

		dbContext.Environments.Add(environment);

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "environment.created",
					targetType: "Environment",
					targetIdentifier: environment.Id.ToString(),
					targetDisplayName: environment.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["slug"] = environment.Slug,
						["isProtected"] = environment.IsProtected
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);

		return Results.Created($"/api/v1/environments/{environment.Id}", ToEnvironmentResponse(environment));
	})
	.RequirePermission(PermissionCatalog.EnvironmentsWrite, InstallationScopePath());

app.MapPatch(
	"/api/v1/environments/{environmentId:guid}",
	async (
		Guid environmentId,
		UpdateEnvironmentRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var environment = await dbContext.Environments.FirstOrDefaultAsync(x => x.Id == environmentId, cancellationToken);
		if (environment is null)
		{
			return Results.Problem(
				title: "Environment not found",
				detail: $"Environment '{environmentId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedSlug = NormalizeSlug(request.Slug, normalizedName);
		if (normalizedSlug.Length == 0)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must contain at least one letter or number."];
		}
		else if (normalizedSlug.Length > 128)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must be 128 characters or fewer."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		if (await dbContext.Environments.AnyAsync(
				x => x.Id != environmentId && x.Name.ToUpper() == normalizedNameUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Environment name conflict",
				detail: $"Environment '{normalizedName}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.Environments.AnyAsync(
				x => x.Id != environmentId && x.Slug == normalizedSlug,
				cancellationToken))
		{
			return Results.Problem(
				title: "Environment slug conflict",
				detail: $"Environment slug '{normalizedSlug}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var previousName = environment.Name;
		var previousSlug = environment.Slug;
		var previousIsProtected = environment.IsProtected;
		var now = DateTimeOffset.UtcNow;

		environment.Name = normalizedName;
		environment.Slug = normalizedSlug;
		environment.Description = NormalizeOptionalText(request.Description);
		environment.IsProtected = request.IsProtected;
		environment.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "environment.updated",
					targetType: "Environment",
					targetIdentifier: environment.Id.ToString(),
					targetDisplayName: environment.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["previousName"] = previousName,
						["previousSlug"] = previousSlug,
						["previousIsProtected"] = previousIsProtected,
						["isProtected"] = environment.IsProtected
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);

		return Results.Ok(ToEnvironmentResponse(environment));
	})
	.RequirePermission(PermissionCatalog.EnvironmentsWrite, InstallationScopePath());

app.MapDelete(
	"/api/v1/environments/{environmentId:guid}",
	async (
		Guid environmentId,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var environment = await dbContext.Environments.FirstOrDefaultAsync(x => x.Id == environmentId, cancellationToken);
		if (environment is null)
		{
			return Results.Problem(
				title: "Environment not found",
				detail: $"Environment '{environmentId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var now = DateTimeOffset.UtcNow;
		environment.IsDeleted = true;
		environment.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "environment.deleted",
					targetType: "Environment",
					targetIdentifier: environment.Id.ToString(),
					targetDisplayName: environment.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["slug"] = environment.Slug,
						["isProtected"] = environment.IsProtected
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.NoContent();
	})
	.RequirePermission(PermissionCatalog.EnvironmentsWrite, InstallationScopePath());

app.MapGet(
	"/api/v1/node-groups",
	async (
		Guid? environmentId,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var query = dbContext.NodeGroups.AsNoTracking();
		if (environmentId.HasValue)
		{
			query = query.Where(x => x.EnvironmentId == environmentId.Value);
		}

		var nodeGroups = await query
			.OrderBy(x => x.Name)
			.Select(x => new NodeGroupSummaryResponse(
				x.Id,
				x.EnvironmentId,
				x.Name,
				x.Slug,
				x.Description,
				x.CreatedAtUtc,
				x.UpdatedAtUtc))
			.ToListAsync(cancellationToken);

		return Results.Ok(nodeGroups);
	})
	.RequirePermission(PermissionCatalog.NodeGroupsRead, InstallationScopePath());

app.MapPost(
	"/api/v1/node-groups",
	async (
		CreateNodeGroupRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.EnvironmentId == Guid.Empty)
		{
			validationErrors[nameof(request.EnvironmentId)] = ["EnvironmentId is required."];
		}

		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedSlug = NormalizeSlug(request.Slug, normalizedName);
		if (normalizedSlug.Length == 0)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must contain at least one letter or number."];
		}
		else if (normalizedSlug.Length > 128)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must be 128 characters or fewer."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var environment = await dbContext.Environments
			.AsNoTracking()
			.Where(x => x.Id == request.EnvironmentId)
			.Select(x => new { x.Id, x.Name })
			.FirstOrDefaultAsync(cancellationToken);

		if (environment is null)
		{
			return Results.Problem(
				title: "Environment not found",
				detail: $"Environment '{request.EnvironmentId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		if (await dbContext.NodeGroups.AnyAsync(
				x => x.EnvironmentId == request.EnvironmentId && x.Name.ToUpper() == normalizedNameUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Node group name conflict",
				detail: $"Node group '{normalizedName}' already exists in environment '{environment.Name}'.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.NodeGroups.AnyAsync(
				x => x.EnvironmentId == request.EnvironmentId && x.Slug == normalizedSlug,
				cancellationToken))
		{
			return Results.Problem(
				title: "Node group slug conflict",
				detail: $"Node group slug '{normalizedSlug}' already exists in environment '{environment.Name}'.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var now = DateTimeOffset.UtcNow;
		var nodeGroup = new NodeGroupDefinition
		{
			Id = Guid.NewGuid(),
			EnvironmentId = request.EnvironmentId,
			Name = normalizedName,
			Slug = normalizedSlug,
			Description = NormalizeOptionalText(request.Description),
			CreatedAtUtc = now,
			UpdatedAtUtc = now
		};

		dbContext.NodeGroups.Add(nodeGroup);

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "nodeGroup.created",
					targetType: "NodeGroup",
					targetIdentifier: nodeGroup.Id.ToString(),
					targetDisplayName: nodeGroup.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["environmentId"] = nodeGroup.EnvironmentId,
						["slug"] = nodeGroup.Slug
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);

		return Results.Created($"/api/v1/node-groups/{nodeGroup.Id}", ToNodeGroupResponse(nodeGroup));
	})
	.RequirePermission(PermissionCatalog.NodeGroupsWrite, InstallationScopePath());

app.MapPatch(
	"/api/v1/node-groups/{nodeGroupId:guid}",
	async (
		Guid nodeGroupId,
		UpdateNodeGroupRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var nodeGroup = await dbContext.NodeGroups.FirstOrDefaultAsync(x => x.Id == nodeGroupId, cancellationToken);
		if (nodeGroup is null)
		{
			return Results.Problem(
				title: "Node group not found",
				detail: $"Node group '{nodeGroupId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedSlug = NormalizeSlug(request.Slug, normalizedName);
		if (normalizedSlug.Length == 0)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must contain at least one letter or number."];
		}
		else if (normalizedSlug.Length > 128)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must be 128 characters or fewer."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		if (await dbContext.NodeGroups.AnyAsync(
				x => x.Id != nodeGroupId && x.EnvironmentId == nodeGroup.EnvironmentId && x.Name.ToUpper() == normalizedNameUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Node group name conflict",
				detail: $"Node group '{normalizedName}' already exists in the selected environment.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.NodeGroups.AnyAsync(
				x => x.Id != nodeGroupId && x.EnvironmentId == nodeGroup.EnvironmentId && x.Slug == normalizedSlug,
				cancellationToken))
		{
			return Results.Problem(
				title: "Node group slug conflict",
				detail: $"Node group slug '{normalizedSlug}' already exists in the selected environment.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var previousName = nodeGroup.Name;
		var previousSlug = nodeGroup.Slug;
		var now = DateTimeOffset.UtcNow;

		nodeGroup.Name = normalizedName;
		nodeGroup.Slug = normalizedSlug;
		nodeGroup.Description = NormalizeOptionalText(request.Description);
		nodeGroup.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "nodeGroup.updated",
					targetType: "NodeGroup",
					targetIdentifier: nodeGroup.Id.ToString(),
					targetDisplayName: nodeGroup.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["previousName"] = previousName,
						["previousSlug"] = previousSlug,
						["slug"] = nodeGroup.Slug
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Ok(ToNodeGroupResponse(nodeGroup));
	})
	.RequirePermission(PermissionCatalog.NodeGroupsWrite, InstallationScopePath());

app.MapDelete(
	"/api/v1/node-groups/{nodeGroupId:guid}",
	async (
		Guid nodeGroupId,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var nodeGroup = await dbContext.NodeGroups.FirstOrDefaultAsync(x => x.Id == nodeGroupId, cancellationToken);
		if (nodeGroup is null)
		{
			return Results.Problem(
				title: "Node group not found",
				detail: $"Node group '{nodeGroupId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var now = DateTimeOffset.UtcNow;
		nodeGroup.IsDeleted = true;
		nodeGroup.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "nodeGroup.deleted",
					targetType: "NodeGroup",
					targetIdentifier: nodeGroup.Id.ToString(),
					targetDisplayName: nodeGroup.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["slug"] = nodeGroup.Slug,
						["environmentId"] = nodeGroup.EnvironmentId
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.NoContent();
	})
	.RequirePermission(PermissionCatalog.NodeGroupsWrite, InstallationScopePath());

app.MapGet(
	"/api/v1/nodes",
	async (
		Guid? environmentId,
		Guid? nodeGroupId,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var query = dbContext.ManagedNodes.AsNoTracking();
		if (environmentId.HasValue)
		{
			query = query.Where(x => x.EnvironmentId == environmentId.Value);
		}

		if (nodeGroupId.HasValue)
		{
			query = query.Where(x => x.NodeGroupId == nodeGroupId.Value);
		}

		var nodes = await query
			.OrderBy(x => x.Name)
			.Select(x => new ManagedNodeSummaryResponse(
				x.Id,
				x.EnvironmentId,
				x.NodeGroupId,
				x.Name,
				x.Hostname,
				x.Platform,
				x.Status,
				x.LastSeenAtUtc,
				x.AgentVersion,
				x.RolloutPolicyDefault,
				x.CreatedAtUtc,
				x.UpdatedAtUtc))
			.ToListAsync(cancellationToken);

		return Results.Ok(nodes);
	})
	.RequirePermission(PermissionCatalog.NodesRead, InstallationScopePath());

app.MapPost(
	"/api/v1/nodes",
	async (
		CreateManagedNodeRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.EnvironmentId == Guid.Empty)
		{
			validationErrors[nameof(request.EnvironmentId)] = ["EnvironmentId is required."];
		}

		if (request.NodeGroupId == Guid.Empty)
		{
			validationErrors[nameof(request.NodeGroupId)] = ["NodeGroupId must be omitted or a valid GUID."];
		}

		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedHostname = NormalizeHostname(request.Hostname);
		if (normalizedHostname.Length == 0)
		{
			validationErrors[nameof(request.Hostname)] = ["Hostname is required."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var environment = await dbContext.Environments
			.AsNoTracking()
			.Where(x => x.Id == request.EnvironmentId)
			.Select(x => new { x.Id, x.Name })
			.FirstOrDefaultAsync(cancellationToken);

		if (environment is null)
		{
			return Results.Problem(
				title: "Environment not found",
				detail: $"Environment '{request.EnvironmentId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		if (request.NodeGroupId.HasValue)
		{
			var nodeGroupExists = await dbContext.NodeGroups.AnyAsync(
				x => x.Id == request.NodeGroupId.Value && x.EnvironmentId == request.EnvironmentId,
				cancellationToken);

			if (!nodeGroupExists)
			{
				return Results.Problem(
					title: "Node group not found",
					detail: $"Node group '{request.NodeGroupId}' does not exist in environment '{environment.Name}'.",
					statusCode: StatusCodes.Status404NotFound);
			}
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		if (await dbContext.ManagedNodes.AnyAsync(
				x => x.EnvironmentId == request.EnvironmentId && x.Name.ToUpper() == normalizedNameUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Node name conflict",
				detail: $"Node '{normalizedName}' already exists in environment '{environment.Name}'.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.ManagedNodes.AnyAsync(x => x.Hostname == normalizedHostname, cancellationToken))
		{
			return Results.Problem(
				title: "Node hostname conflict",
				detail: $"Node hostname '{normalizedHostname}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var now = DateTimeOffset.UtcNow;
		var node = new ManagedNodeRecord
		{
			Id = Guid.NewGuid(),
			EnvironmentId = request.EnvironmentId,
			NodeGroupId = request.NodeGroupId,
			Name = normalizedName,
			Hostname = normalizedHostname,
			Platform = NormalizeOptionalTextOrDefault(request.Platform, "Unknown"),
			Status = NormalizeOptionalTextOrDefault(request.Status, "Unknown"),
			LastSeenAtUtc = request.LastSeenAtUtc,
			AgentVersion = NormalizeOptionalText(request.AgentVersion),
			RolloutPolicyDefault = NormalizeOptionalText(request.RolloutPolicyDefault),
			CreatedAtUtc = now,
			UpdatedAtUtc = now
		};

		dbContext.ManagedNodes.Add(node);

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "node.created",
					targetType: "ManagedNode",
					targetIdentifier: node.Id.ToString(),
					targetDisplayName: node.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["environmentId"] = node.EnvironmentId,
						["nodeGroupId"] = node.NodeGroupId,
						["hostname"] = node.Hostname,
						["status"] = node.Status
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Created($"/api/v1/nodes/{node.Id}", ToManagedNodeResponse(node));
	})
	.RequirePermission(PermissionCatalog.NodesWrite, InstallationScopePath());

app.MapPatch(
	"/api/v1/nodes/{nodeId:guid}",
	async (
		Guid nodeId,
		UpdateManagedNodeRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var node = await dbContext.ManagedNodes.FirstOrDefaultAsync(x => x.Id == nodeId, cancellationToken);
		if (node is null)
		{
			return Results.Problem(
				title: "Node not found",
				detail: $"Node '{nodeId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.NodeGroupId == Guid.Empty)
		{
			validationErrors[nameof(request.NodeGroupId)] = ["NodeGroupId must be omitted or a valid GUID."];
		}

		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedHostname = NormalizeHostname(request.Hostname);
		if (normalizedHostname.Length == 0)
		{
			validationErrors[nameof(request.Hostname)] = ["Hostname is required."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		if (request.NodeGroupId.HasValue)
		{
			var nodeGroupExists = await dbContext.NodeGroups.AnyAsync(
				x => x.Id == request.NodeGroupId.Value && x.EnvironmentId == node.EnvironmentId,
				cancellationToken);

			if (!nodeGroupExists)
			{
				return Results.Problem(
					title: "Node group not found",
					detail: $"Node group '{request.NodeGroupId}' does not exist in the selected environment.",
					statusCode: StatusCodes.Status404NotFound);
			}
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		if (await dbContext.ManagedNodes.AnyAsync(
				x => x.Id != nodeId && x.EnvironmentId == node.EnvironmentId && x.Name.ToUpper() == normalizedNameUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Node name conflict",
				detail: $"Node '{normalizedName}' already exists in the selected environment.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.ManagedNodes.AnyAsync(
				x => x.Id != nodeId && x.Hostname == normalizedHostname,
				cancellationToken))
		{
			return Results.Problem(
				title: "Node hostname conflict",
				detail: $"Node hostname '{normalizedHostname}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var previousName = node.Name;
		var previousHostname = node.Hostname;
		var previousNodeGroupId = node.NodeGroupId;
		var previousStatus = node.Status;
		var now = DateTimeOffset.UtcNow;

		node.NodeGroupId = request.NodeGroupId;
		node.Name = normalizedName;
		node.Hostname = normalizedHostname;
		node.Platform = NormalizeOptionalTextOrDefault(request.Platform, "Unknown");
		node.Status = NormalizeOptionalTextOrDefault(request.Status, "Unknown");
		node.LastSeenAtUtc = request.LastSeenAtUtc;
		node.AgentVersion = NormalizeOptionalText(request.AgentVersion);
		node.RolloutPolicyDefault = NormalizeOptionalText(request.RolloutPolicyDefault);
		node.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "node.updated",
					targetType: "ManagedNode",
					targetIdentifier: node.Id.ToString(),
					targetDisplayName: node.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["previousName"] = previousName,
						["previousHostname"] = previousHostname,
						["previousNodeGroupId"] = previousNodeGroupId,
						["previousStatus"] = previousStatus,
						["nodeGroupId"] = node.NodeGroupId,
						["status"] = node.Status
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Ok(ToManagedNodeResponse(node));
	})
	.RequirePermission(PermissionCatalog.NodesWrite, InstallationScopePath());

app.MapDelete(
	"/api/v1/nodes/{nodeId:guid}",
	async (
		Guid nodeId,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var node = await dbContext.ManagedNodes.FirstOrDefaultAsync(x => x.Id == nodeId, cancellationToken);
		if (node is null)
		{
			return Results.Problem(
				title: "Node not found",
				detail: $"Node '{nodeId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var now = DateTimeOffset.UtcNow;
		node.IsDeleted = true;
		node.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "node.deleted",
					targetType: "ManagedNode",
					targetIdentifier: node.Id.ToString(),
					targetDisplayName: node.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["environmentId"] = node.EnvironmentId,
						["nodeGroupId"] = node.NodeGroupId,
						["hostname"] = node.Hostname
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.NoContent();
	})
	.RequirePermission(PermissionCatalog.NodesWrite, InstallationScopePath());

app.MapGet(
	"/api/v1/applications",
	async (
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var applications = await dbContext.Applications
			.AsNoTracking()
			.OrderBy(x => x.Name)
			.Select(x => new ApplicationSummaryResponse(
				x.Id,
				x.Name,
				x.Slug,
				x.Description,
				x.DefaultIntegrationMode,
				x.CreatedAtUtc,
				x.UpdatedAtUtc))
			.ToListAsync(cancellationToken);

		return Results.Ok(applications);
	})
	.RequirePermission(PermissionCatalog.ApplicationsRead, InstallationScopePath());

app.MapPost(
	"/api/v1/applications",
	async (
		CreateApplicationRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedSlug = NormalizeSlug(request.Slug, normalizedName);
		if (normalizedSlug.Length == 0)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must contain at least one letter or number."];
		}
		else if (normalizedSlug.Length > 128)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must be 128 characters or fewer."];
		}

		var normalizedIntegrationMode = NormalizeCatalogKeyword(request.DefaultIntegrationMode, "runtime-api");
		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		if (await dbContext.Applications.AnyAsync(x => x.Name.ToUpper() == normalizedNameUpper, cancellationToken))
		{
			return Results.Problem(
				title: "Application name conflict",
				detail: $"Application '{normalizedName}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.Applications.AnyAsync(x => x.Slug == normalizedSlug, cancellationToken))
		{
			return Results.Problem(
				title: "Application slug conflict",
				detail: $"Application slug '{normalizedSlug}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var now = DateTimeOffset.UtcNow;
		var application = new ApplicationDefinition
		{
			Id = Guid.NewGuid(),
			Name = normalizedName,
			Slug = normalizedSlug,
			Description = NormalizeOptionalText(request.Description),
			DefaultIntegrationMode = normalizedIntegrationMode,
			CreatedAtUtc = now,
			UpdatedAtUtc = now
		};

		dbContext.Applications.Add(application);

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "application.created",
					targetType: "Application",
					targetIdentifier: application.Id.ToString(),
					targetDisplayName: application.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["slug"] = application.Slug,
						["defaultIntegrationMode"] = application.DefaultIntegrationMode
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Created($"/api/v1/applications/{application.Id}", ToApplicationResponse(application));
	})
	.RequirePermission(PermissionCatalog.ApplicationsWrite, InstallationScopePath());

app.MapPatch(
	"/api/v1/applications/{applicationId:guid}",
	async (
		Guid applicationId,
		UpdateApplicationRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var application = await dbContext.Applications.FirstOrDefaultAsync(x => x.Id == applicationId, cancellationToken);
		if (application is null)
		{
			return Results.Problem(
				title: "Application not found",
				detail: $"Application '{applicationId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedSlug = NormalizeSlug(request.Slug, normalizedName);
		if (normalizedSlug.Length == 0)
		{
			validationErrors[nameof(request.Slug)] = ["Slug must contain at least one letter or number."];
		}

		var normalizedIntegrationMode = NormalizeCatalogKeyword(request.DefaultIntegrationMode, "runtime-api");
		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		if (await dbContext.Applications.AnyAsync(
				x => x.Id != applicationId && x.Name.ToUpper() == normalizedNameUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Application name conflict",
				detail: $"Application '{normalizedName}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.Applications.AnyAsync(
				x => x.Id != applicationId && x.Slug == normalizedSlug,
				cancellationToken))
		{
			return Results.Problem(
				title: "Application slug conflict",
				detail: $"Application slug '{normalizedSlug}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var previousName = application.Name;
		var previousSlug = application.Slug;
		var previousIntegrationMode = application.DefaultIntegrationMode;
		var now = DateTimeOffset.UtcNow;

		application.Name = normalizedName;
		application.Slug = normalizedSlug;
		application.Description = NormalizeOptionalText(request.Description);
		application.DefaultIntegrationMode = normalizedIntegrationMode;
		application.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "application.updated",
					targetType: "Application",
					targetIdentifier: application.Id.ToString(),
					targetDisplayName: application.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["previousName"] = previousName,
						["previousSlug"] = previousSlug,
						["previousDefaultIntegrationMode"] = previousIntegrationMode,
						["slug"] = application.Slug,
						["defaultIntegrationMode"] = application.DefaultIntegrationMode
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Ok(ToApplicationResponse(application));
	})
	.RequirePermission(PermissionCatalog.ApplicationsWrite, InstallationScopePath());

app.MapDelete(
	"/api/v1/applications/{applicationId:guid}",
	async (
		Guid applicationId,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var application = await dbContext.Applications.FirstOrDefaultAsync(x => x.Id == applicationId, cancellationToken);
		if (application is null)
		{
			return Results.Problem(
				title: "Application not found",
				detail: $"Application '{applicationId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var hasDependencies = await dbContext.Namespaces.AnyAsync(x => x.ApplicationId == applicationId, cancellationToken)
			|| await dbContext.ConfigItems.AnyAsync(x => x.ApplicationId == applicationId, cancellationToken)
			|| await dbContext.ApplicationAssignments.AnyAsync(x => x.ApplicationId == applicationId, cancellationToken);

		if (hasDependencies)
		{
			return Results.Problem(
				title: "Application has dependent resources",
				detail: $"Application '{application.Name}' still has namespaces, config items, or assignments.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var now = DateTimeOffset.UtcNow;
		application.IsDeleted = true;
		application.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "application.deleted",
					targetType: "Application",
					targetIdentifier: application.Id.ToString(),
					targetDisplayName: application.Name,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["slug"] = application.Slug
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.NoContent();
	})
	.RequirePermission(PermissionCatalog.ApplicationsWrite, InstallationScopePath());

app.MapGet(
	"/api/v1/namespaces",
	async (
		Guid? applicationId,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var query = dbContext.Namespaces.AsNoTracking();
		if (applicationId.HasValue)
		{
			query = query.Where(x => x.ApplicationId == applicationId.Value);
		}

		var namespaces = await query
			.OrderBy(x => x.Path)
			.Select(x => new NamespaceSummaryResponse(
				x.Id,
				x.ApplicationId,
				x.Name,
				x.Path,
				x.Description,
				x.CreatedAtUtc,
				x.UpdatedAtUtc))
			.ToListAsync(cancellationToken);

		return Results.Ok(namespaces);
	})
	.RequirePermission(PermissionCatalog.NamespacesRead, InstallationScopePath());

app.MapPost(
	"/api/v1/namespaces",
	async (
		CreateNamespaceRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.ApplicationId == Guid.Empty)
		{
			validationErrors[nameof(request.ApplicationId)] = ["ApplicationId is required."];
		}

		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedPath = NormalizeNamespacePath(request.Path, normalizedName);
		if (normalizedPath.Length == 0)
		{
			validationErrors[nameof(request.Path)] = ["Path must contain at least one segment."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var application = await dbContext.Applications
			.AsNoTracking()
			.Where(x => x.Id == request.ApplicationId)
			.Select(x => new { x.Id, x.Name })
			.FirstOrDefaultAsync(cancellationToken);

		if (application is null)
		{
			return Results.Problem(
				title: "Application not found",
				detail: $"Application '{request.ApplicationId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		var normalizedPathUpper = normalizedPath.ToUpperInvariant();
		if (await dbContext.Namespaces.AnyAsync(
				x => x.ApplicationId == request.ApplicationId && x.Name.ToUpper() == normalizedNameUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Namespace name conflict",
				detail: $"Namespace '{normalizedName}' already exists in application '{application.Name}'.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.Namespaces.AnyAsync(
				x => x.ApplicationId == request.ApplicationId && x.Path.ToUpper() == normalizedPathUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Namespace path conflict",
				detail: $"Namespace path '{normalizedPath}' already exists in application '{application.Name}'.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var now = DateTimeOffset.UtcNow;
		var catalogNamespace = new NamespaceDefinition
		{
			Id = Guid.NewGuid(),
			ApplicationId = request.ApplicationId,
			Name = normalizedName,
			Path = normalizedPath,
			Description = NormalizeOptionalText(request.Description),
			CreatedAtUtc = now,
			UpdatedAtUtc = now
		};

		dbContext.Namespaces.Add(catalogNamespace);

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "namespace.created",
					targetType: "Namespace",
					targetIdentifier: catalogNamespace.Id.ToString(),
					targetDisplayName: catalogNamespace.Path,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["applicationId"] = catalogNamespace.ApplicationId,
						["path"] = catalogNamespace.Path
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Created($"/api/v1/namespaces/{catalogNamespace.Id}", ToNamespaceResponse(catalogNamespace));
	})
	.RequirePermission(PermissionCatalog.NamespacesWrite, InstallationScopePath());

app.MapPatch(
	"/api/v1/namespaces/{namespaceId:guid}",
	async (
		Guid namespaceId,
		UpdateNamespaceRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var catalogNamespace = await dbContext.Namespaces.FirstOrDefaultAsync(x => x.Id == namespaceId, cancellationToken);
		if (catalogNamespace is null)
		{
			return Results.Problem(
				title: "Namespace not found",
				detail: $"Namespace '{namespaceId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		var normalizedName = NormalizeRequiredText(request.Name);
		if (normalizedName.Length == 0)
		{
			validationErrors[nameof(request.Name)] = ["Name is required."];
		}

		var normalizedPath = NormalizeNamespacePath(request.Path, normalizedName);
		if (normalizedPath.Length == 0)
		{
			validationErrors[nameof(request.Path)] = ["Path must contain at least one segment."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var normalizedNameUpper = normalizedName.ToUpperInvariant();
		var normalizedPathUpper = normalizedPath.ToUpperInvariant();
		if (await dbContext.Namespaces.AnyAsync(
				x => x.Id != namespaceId && x.ApplicationId == catalogNamespace.ApplicationId && x.Name.ToUpper() == normalizedNameUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Namespace name conflict",
				detail: $"Namespace '{normalizedName}' already exists in the selected application.",
				statusCode: StatusCodes.Status409Conflict);
		}

		if (await dbContext.Namespaces.AnyAsync(
				x => x.Id != namespaceId && x.ApplicationId == catalogNamespace.ApplicationId && x.Path.ToUpper() == normalizedPathUpper,
				cancellationToken))
		{
			return Results.Problem(
				title: "Namespace path conflict",
				detail: $"Namespace path '{normalizedPath}' already exists in the selected application.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var previousName = catalogNamespace.Name;
		var previousPath = catalogNamespace.Path;
		var now = DateTimeOffset.UtcNow;

		catalogNamespace.Name = normalizedName;
		catalogNamespace.Path = normalizedPath;
		catalogNamespace.Description = NormalizeOptionalText(request.Description);
		catalogNamespace.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "namespace.updated",
					targetType: "Namespace",
					targetIdentifier: catalogNamespace.Id.ToString(),
					targetDisplayName: catalogNamespace.Path,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["previousName"] = previousName,
						["previousPath"] = previousPath,
						["path"] = catalogNamespace.Path
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Ok(ToNamespaceResponse(catalogNamespace));
	})
	.RequirePermission(PermissionCatalog.NamespacesWrite, InstallationScopePath());

app.MapDelete(
	"/api/v1/namespaces/{namespaceId:guid}",
	async (
		Guid namespaceId,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var catalogNamespace = await dbContext.Namespaces.FirstOrDefaultAsync(x => x.Id == namespaceId, cancellationToken);
		if (catalogNamespace is null)
		{
			return Results.Problem(
				title: "Namespace not found",
				detail: $"Namespace '{namespaceId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		if (await dbContext.ConfigItems.AnyAsync(x => x.NamespaceId == namespaceId, cancellationToken))
		{
			return Results.Problem(
				title: "Namespace has dependent config items",
				detail: $"Namespace '{catalogNamespace.Path}' still has config items.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var now = DateTimeOffset.UtcNow;
		catalogNamespace.IsDeleted = true;
		catalogNamespace.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "namespace.deleted",
					targetType: "Namespace",
					targetIdentifier: catalogNamespace.Id.ToString(),
					targetDisplayName: catalogNamespace.Path,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["applicationId"] = catalogNamespace.ApplicationId,
						["path"] = catalogNamespace.Path
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.NoContent();
	})
	.RequirePermission(PermissionCatalog.NamespacesWrite, InstallationScopePath());

app.MapGet(
	"/api/v1/application-assignments",
	async (
		Guid? applicationId,
		Guid? environmentId,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var query = dbContext.ApplicationAssignments.AsNoTracking();
		if (applicationId.HasValue)
		{
			query = query.Where(x => x.ApplicationId == applicationId.Value);
		}

		if (environmentId.HasValue)
		{
			query = query.Where(x => x.EnvironmentId == environmentId.Value);
		}

		var assignments = await query
			.OrderBy(x => x.CreatedAtUtc)
			.Select(x => new ApplicationAssignmentResponse(
				x.Id,
				x.ApplicationId,
				x.EnvironmentId,
				x.NodeGroupId,
				x.ManagedNodeId,
				x.Enabled,
				x.CreatedAtUtc))
			.ToListAsync(cancellationToken);

		return Results.Ok(assignments);
	})
	.RequirePermission(PermissionCatalog.ApplicationsRead, InstallationScopePath());

app.MapPost(
	"/api/v1/application-assignments",
	async (
		CreateApplicationAssignmentRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.ApplicationId == Guid.Empty)
		{
			validationErrors[nameof(request.ApplicationId)] = ["ApplicationId is required."];
		}

		if (request.EnvironmentId == Guid.Empty)
		{
			validationErrors[nameof(request.EnvironmentId)] = ["EnvironmentId is required."];
		}

		if (request.NodeGroupId.HasValue == request.ManagedNodeId.HasValue)
		{
			validationErrors[nameof(request.NodeGroupId)] = ["Specify either NodeGroupId or ManagedNodeId."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var application = await dbContext.Applications
			.AsNoTracking()
			.Where(x => x.Id == request.ApplicationId)
			.Select(x => new { x.Id, x.Name })
			.FirstOrDefaultAsync(cancellationToken);

		if (application is null)
		{
			return Results.Problem(
				title: "Application not found",
				detail: $"Application '{request.ApplicationId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var environment = await dbContext.Environments
			.AsNoTracking()
			.Where(x => x.Id == request.EnvironmentId)
			.Select(x => new { x.Id, x.Name })
			.FirstOrDefaultAsync(cancellationToken);

		if (environment is null)
		{
			return Results.Problem(
				title: "Environment not found",
				detail: $"Environment '{request.EnvironmentId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		string assignmentTargetType;
		string assignmentTargetName;
		if (request.NodeGroupId.HasValue)
		{
			var nodeGroup = await dbContext.NodeGroups
				.AsNoTracking()
				.Where(x => x.Id == request.NodeGroupId.Value && x.EnvironmentId == request.EnvironmentId)
				.Select(x => new { x.Id, x.Name })
				.FirstOrDefaultAsync(cancellationToken);

			if (nodeGroup is null)
			{
				return Results.Problem(
					title: "Node group not found",
					detail: $"Node group '{request.NodeGroupId}' does not exist in environment '{environment.Name}'.",
					statusCode: StatusCodes.Status404NotFound);
			}

			assignmentTargetType = "NodeGroup";
			assignmentTargetName = nodeGroup.Name;
		}
		else
		{
			var node = await dbContext.ManagedNodes
				.AsNoTracking()
				.Where(x => x.Id == request.ManagedNodeId!.Value && x.EnvironmentId == request.EnvironmentId)
				.Select(x => new { x.Id, x.Name })
				.FirstOrDefaultAsync(cancellationToken);

			if (node is null)
			{
				return Results.Problem(
					title: "Node not found",
					detail: $"Node '{request.ManagedNodeId}' does not exist in environment '{environment.Name}'.",
					statusCode: StatusCodes.Status404NotFound);
			}

			assignmentTargetType = "ManagedNode";
			assignmentTargetName = node.Name;
		}

		var assignmentExists = await dbContext.ApplicationAssignments.AnyAsync(
			x => x.ApplicationId == request.ApplicationId
				&& x.EnvironmentId == request.EnvironmentId
				&& x.NodeGroupId == request.NodeGroupId
				&& x.ManagedNodeId == request.ManagedNodeId,
			cancellationToken);

		if (assignmentExists)
		{
			return Results.Conflict();
		}

		var now = DateTimeOffset.UtcNow;
		var assignment = new ApplicationAssignment
		{
			Id = Guid.NewGuid(),
			ApplicationId = request.ApplicationId,
			EnvironmentId = request.EnvironmentId,
			NodeGroupId = request.NodeGroupId,
			ManagedNodeId = request.ManagedNodeId,
			Enabled = request.Enabled,
			CreatedAtUtc = now
		};

		dbContext.ApplicationAssignments.Add(assignment);

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "application.assignment.created",
					targetType: "ApplicationAssignment",
					targetIdentifier: assignment.Id.ToString(),
					targetDisplayName: $"{application.Name} -> {assignmentTargetName}",
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["applicationId"] = assignment.ApplicationId,
						["environmentId"] = assignment.EnvironmentId,
						["nodeGroupId"] = assignment.NodeGroupId,
						["managedNodeId"] = assignment.ManagedNodeId,
						["assignmentTargetType"] = assignmentTargetType,
						["enabled"] = assignment.Enabled
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Created(
			$"/api/v1/application-assignments/{assignment.Id}",
			ToApplicationAssignmentResponse(assignment));
	})
	.RequirePermission(PermissionCatalog.ApplicationsWrite, InstallationScopePath());

app.MapGet(
	"/api/v1/config-items",
	async (
		Guid? applicationId,
		Guid? namespaceId,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var query = dbContext.ConfigItems.AsNoTracking();
		if (applicationId.HasValue)
		{
			query = query.Where(x => x.ApplicationId == applicationId.Value);
		}

		if (namespaceId.HasValue)
		{
			query = query.Where(x => x.NamespaceId == namespaceId.Value);
		}

		var configItems = await query
			.OrderBy(x => x.FullPath)
			.Select(x => new ConfigItemSummaryResponse(
				x.Id,
				x.ApplicationId,
				x.NamespaceId,
				x.Key,
				x.FullPath,
				x.ValueType,
				x.IsSecret,
				x.IsRequired,
				x.DefaultRolloutPolicy,
				x.ValidationSchemaJson,
				x.Description,
				x.CreatedAtUtc,
				x.UpdatedAtUtc))
			.ToListAsync(cancellationToken);

		return Results.Ok(configItems);
	})
	.RequirePermission(PermissionCatalog.ConfigReadMasked, InstallationScopePath());

app.MapPost(
	"/api/v1/config-items",
	async (
		CreateConfigItemRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.NamespaceId == Guid.Empty)
		{
			validationErrors[nameof(request.NamespaceId)] = ["NamespaceId is required."];
		}

		var normalizedKey = NormalizeConfigItemKey(request.Key);
		if (normalizedKey.Length == 0)
		{
			validationErrors[nameof(request.Key)] = ["Key is required."];
		}
		else if (ContainsPathSeparator(normalizedKey))
		{
			validationErrors[nameof(request.Key)] = ["Key must be a single segment and cannot contain path separators."];
		}

		var normalizedValueType = NormalizeCatalogKeyword(request.ValueType, "string");
		var normalizedRolloutPolicy = NormalizeCatalogKeyword(request.DefaultRolloutPolicy, "immediate");
		if (!TryNormalizeJsonDocument(request.ValidationSchemaJson, out var normalizedValidationSchemaJson, out var jsonError))
		{
			validationErrors[nameof(request.ValidationSchemaJson)] = [$"ValidationSchemaJson must be valid JSON. {jsonError}"];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var catalogNamespace = await dbContext.Namespaces
			.AsNoTracking()
			.Where(x => x.Id == request.NamespaceId)
			.Select(x => new { x.Id, x.ApplicationId, x.Path })
			.FirstOrDefaultAsync(cancellationToken);

		if (catalogNamespace is null)
		{
			return Results.Problem(
				title: "Namespace not found",
				detail: $"Namespace '{request.NamespaceId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var fullPath = BuildConfigItemFullPath(catalogNamespace.Path, normalizedKey);
		if (await dbContext.ConfigItems.AnyAsync(
				x => x.ApplicationId == catalogNamespace.ApplicationId && x.FullPath == fullPath,
				cancellationToken))
		{
			return Results.Problem(
				title: "Config item path conflict",
				detail: $"Config item '{fullPath}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var now = DateTimeOffset.UtcNow;
		var configItem = new ConfigItemDefinition
		{
			Id = Guid.NewGuid(),
			ApplicationId = catalogNamespace.ApplicationId,
			NamespaceId = catalogNamespace.Id,
			Key = normalizedKey,
			FullPath = fullPath,
			ValueType = normalizedValueType,
			IsSecret = request.IsSecret,
			IsRequired = request.IsRequired,
			DefaultRolloutPolicy = normalizedRolloutPolicy,
			ValidationSchemaJson = normalizedValidationSchemaJson,
			Description = NormalizeOptionalText(request.Description),
			CreatedAtUtc = now,
			UpdatedAtUtc = now
		};

		dbContext.ConfigItems.Add(configItem);

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "configItem.created",
					targetType: "ConfigItem",
					targetIdentifier: configItem.Id.ToString(),
					targetDisplayName: configItem.FullPath,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["applicationId"] = configItem.ApplicationId,
						["namespaceId"] = configItem.NamespaceId,
						["fullPath"] = configItem.FullPath,
						["valueType"] = configItem.ValueType,
						["isSecret"] = configItem.IsSecret
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Created($"/api/v1/config-items/{configItem.Id}", ToConfigItemResponse(configItem));
	})
	.RequirePermission(PermissionCatalog.ConfigWriteDraft, InstallationScopePath());

app.MapPatch(
	"/api/v1/config-items/{configItemId:guid}",
	async (
		Guid configItemId,
		UpdateConfigItemRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var configItem = await dbContext.ConfigItems.FirstOrDefaultAsync(x => x.Id == configItemId, cancellationToken);
		if (configItem is null)
		{
			return Results.Problem(
				title: "Config item not found",
				detail: $"Config item '{configItemId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.NamespaceId == Guid.Empty)
		{
			validationErrors[nameof(request.NamespaceId)] = ["NamespaceId is required."];
		}

		var normalizedKey = NormalizeConfigItemKey(request.Key);
		if (normalizedKey.Length == 0)
		{
			validationErrors[nameof(request.Key)] = ["Key is required."];
		}
		else if (ContainsPathSeparator(normalizedKey))
		{
			validationErrors[nameof(request.Key)] = ["Key must be a single segment and cannot contain path separators."];
		}

		var normalizedValueType = NormalizeCatalogKeyword(request.ValueType, "string");
		var normalizedRolloutPolicy = NormalizeCatalogKeyword(request.DefaultRolloutPolicy, "immediate");
		if (!TryNormalizeJsonDocument(request.ValidationSchemaJson, out var normalizedValidationSchemaJson, out var jsonError))
		{
			validationErrors[nameof(request.ValidationSchemaJson)] = [$"ValidationSchemaJson must be valid JSON. {jsonError}"];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var catalogNamespace = await dbContext.Namespaces
			.AsNoTracking()
			.Where(x => x.Id == request.NamespaceId)
			.Select(x => new { x.Id, x.ApplicationId, x.Path })
			.FirstOrDefaultAsync(cancellationToken);

		if (catalogNamespace is null)
		{
			return Results.Problem(
				title: "Namespace not found",
				detail: $"Namespace '{request.NamespaceId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var fullPath = BuildConfigItemFullPath(catalogNamespace.Path, normalizedKey);
		if (await dbContext.ConfigItems.AnyAsync(
				x => x.Id != configItemId && x.ApplicationId == catalogNamespace.ApplicationId && x.FullPath == fullPath,
				cancellationToken))
		{
			return Results.Problem(
				title: "Config item path conflict",
				detail: $"Config item '{fullPath}' already exists.",
				statusCode: StatusCodes.Status409Conflict);
		}

		var previousNamespaceId = configItem.NamespaceId;
		var previousFullPath = configItem.FullPath;
		var previousValueType = configItem.ValueType;
		var now = DateTimeOffset.UtcNow;

		configItem.ApplicationId = catalogNamespace.ApplicationId;
		configItem.NamespaceId = catalogNamespace.Id;
		configItem.Key = normalizedKey;
		configItem.FullPath = fullPath;
		configItem.ValueType = normalizedValueType;
		configItem.IsSecret = request.IsSecret;
		configItem.IsRequired = request.IsRequired;
		configItem.DefaultRolloutPolicy = normalizedRolloutPolicy;
		configItem.ValidationSchemaJson = normalizedValidationSchemaJson;
		configItem.Description = NormalizeOptionalText(request.Description);
		configItem.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "configItem.updated",
					targetType: "ConfigItem",
					targetIdentifier: configItem.Id.ToString(),
					targetDisplayName: configItem.FullPath,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["previousNamespaceId"] = previousNamespaceId,
						["previousFullPath"] = previousFullPath,
						["previousValueType"] = previousValueType,
						["namespaceId"] = configItem.NamespaceId,
						["fullPath"] = configItem.FullPath,
						["valueType"] = configItem.ValueType
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Ok(ToConfigItemResponse(configItem));
	})
	.RequirePermission(PermissionCatalog.ConfigWriteDraft, InstallationScopePath());

app.MapDelete(
	"/api/v1/config-items/{configItemId:guid}",
	async (
		Guid configItemId,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var configItem = await dbContext.ConfigItems.FirstOrDefaultAsync(x => x.Id == configItemId, cancellationToken);
		if (configItem is null)
		{
			return Results.Problem(
				title: "Config item not found",
				detail: $"Config item '{configItemId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var now = DateTimeOffset.UtcNow;
		configItem.IsDeleted = true;
		configItem.UpdatedAtUtc = now;

		var (actorUserId, actorUsername) = GetAuditActor(user);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "configItem.deleted",
					targetType: "ConfigItem",
					targetIdentifier: configItem.Id.ToString(),
					targetDisplayName: configItem.FullPath,
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["applicationId"] = configItem.ApplicationId,
						["namespaceId"] = configItem.NamespaceId,
						["fullPath"] = configItem.FullPath
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.NoContent();
	})
	.RequirePermission(PermissionCatalog.ConfigDeleteDraft, InstallationScopePath());

app.MapGet(
	"/api/v1/draft-values",
	async (
		Guid? configItemId,
		Guid? applicationId,
		string? scopeType,
		Guid? scopeId,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = new Dictionary<string, string[]>();
		var hasScopeTypeFilter = !string.IsNullOrWhiteSpace(scopeType);
		var parsedScopeType = default(ResourceScopeType);

		if (hasScopeTypeFilter && !TryParseDraftScopeType(scopeType, out parsedScopeType))
		{
			validationErrors[nameof(CreateDraftValueRequest.ScopeType)] = ["ScopeType must be Application, Environment, NodeGroup, ManagedNode, or EmergencyOverride."];
		}

		if (scopeId.HasValue && !hasScopeTypeFilter)
		{
			validationErrors[nameof(CreateDraftValueRequest.ScopeType)] = ["ScopeType is required when ScopeId is provided."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var query = dbContext.DraftValues.AsNoTracking();
		if (configItemId.HasValue)
		{
			query = query.Where(x => x.ConfigItemId == configItemId.Value);
		}

		if (applicationId.HasValue)
		{
			query = query.Where(x => dbContext.ConfigItems.Any(configItem =>
				configItem.Id == x.ConfigItemId && configItem.ApplicationId == applicationId.Value));
		}

		if (hasScopeTypeFilter)
		{
			query = query.Where(x => x.ScopeType == parsedScopeType);
		}

		if (scopeId.HasValue)
		{
			query = query.Where(x => x.ScopeId == scopeId.Value);
		}

		var draftValues = await query
			.OrderBy(x => x.ConfigItemId)
			.ThenBy(x => x.ScopeType)
			.ThenBy(x => x.ScopeId)
			.Select(x => new DraftValueResponse(
				x.Id,
				x.ConfigItemId,
				x.ScopeType.ToString(),
				x.ScopeId,
				x.IsSecret ? null : x.ValueJson,
				x.IsSecret,
				x.IsSecret,
				x.ChangeNote,
				x.UpdatedByUserId,
				x.UpdatedAtUtc))
			.ToListAsync(cancellationToken);

		return Results.Ok(draftValues);
	})
	.RequirePermission(PermissionCatalog.ConfigReadMasked, InstallationScopePath());

app.MapPost(
	"/api/v1/draft-values",
	async (
		CreateDraftValueRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		IDraftValueProtector draftValueProtector,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.ConfigItemId == Guid.Empty)
		{
			validationErrors[nameof(request.ConfigItemId)] = ["ConfigItemId is required."];
		}

		if (request.ScopeId == Guid.Empty)
		{
			validationErrors[nameof(request.ScopeId)] = ["ScopeId is required."];
		}

		if (!TryParseDraftScopeType(request.ScopeType, out var parsedScopeType))
		{
			validationErrors[nameof(request.ScopeType)] = ["ScopeType must be Application, Environment, NodeGroup, ManagedNode, or EmergencyOverride."];
		}

		if (!TryNormalizeJsonDocument(request.ValueJson, out var normalizedValueJson, out var jsonError))
		{
			validationErrors[nameof(request.ValueJson)] = [$"ValueJson must be valid JSON. {jsonError}"];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var configItem = await dbContext.ConfigItems
			.AsNoTracking()
			.FirstOrDefaultAsync(x => x.Id == request.ConfigItemId, cancellationToken);

		if (configItem is null)
		{
			return Results.Problem(
				title: "Config item not found",
				detail: $"Config item '{request.ConfigItemId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var scopeValidationResult = await ValidateDraftScopeAsync(
			parsedScopeType,
			request.ScopeId,
			configItem.ApplicationId,
			dbContext,
			cancellationToken);
		if (scopeValidationResult is not null)
		{
			return scopeValidationResult;
		}

		if (await dbContext.DraftValues.AsNoTracking().AnyAsync(
				x => x.ConfigItemId == request.ConfigItemId && x.ScopeType == parsedScopeType && x.ScopeId == request.ScopeId,
				cancellationToken))
		{
			return Results.Conflict();
		}

		var now = DateTimeOffset.UtcNow;
		var (actorUserId, actorUsername) = GetAuditActor(user);
		var draftValue = new DraftValue
		{
			Id = Guid.NewGuid(),
			ConfigItemId = request.ConfigItemId,
			ScopeType = parsedScopeType,
			ScopeId = request.ScopeId,
			ValueJson = ProtectDraftValueJson(normalizedValueJson, configItem.IsSecret, draftValueProtector),
			IsSecret = configItem.IsSecret,
			ChangeNote = NormalizeOptionalTextOrDefault(request.ChangeNote, "Draft value created."),
			UpdatedByUserId = actorUserId,
			UpdatedAtUtc = now
		};

		dbContext.DraftValues.Add(draftValue);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "draftValue.created",
					targetType: "DraftValue",
					targetIdentifier: draftValue.Id.ToString(),
					targetDisplayName: $"{draftValue.ScopeType}:{draftValue.ScopeId}",
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["configItemId"] = draftValue.ConfigItemId,
						["scopeType"] = draftValue.ScopeType.ToString(),
						["scopeId"] = draftValue.ScopeId,
						["isSecret"] = draftValue.IsSecret
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Created($"/api/v1/draft-values/{draftValue.Id}", ToDraftValueResponse(draftValue));
	})
	.RequirePermission(PermissionCatalog.ConfigWriteDraft, InstallationScopePath());

app.MapPatch(
	"/api/v1/draft-values/{draftValueId:guid}",
	async (
		Guid draftValueId,
		UpdateDraftValueRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		IDraftValueProtector draftValueProtector,
		CancellationToken cancellationToken) =>
	{
		var draftValue = await dbContext.DraftValues.FirstOrDefaultAsync(x => x.Id == draftValueId, cancellationToken);
		if (draftValue is null)
		{
			return Results.Problem(
				title: "Draft value not found",
				detail: $"Draft value '{draftValueId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (!TryNormalizeJsonDocument(request.ValueJson, out var normalizedValueJson, out var jsonError))
		{
			validationErrors[nameof(request.ValueJson)] = [$"ValueJson must be valid JSON. {jsonError}"];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var configItem = await dbContext.ConfigItems
			.AsNoTracking()
			.FirstOrDefaultAsync(x => x.Id == draftValue.ConfigItemId, cancellationToken);

		if (configItem is null)
		{
			return Results.Problem(
				title: "Config item not found",
				detail: $"Config item '{draftValue.ConfigItemId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var now = DateTimeOffset.UtcNow;
		var (actorUserId, actorUsername) = GetAuditActor(user);
		var previousChangeNote = draftValue.ChangeNote;

		draftValue.IsSecret = configItem.IsSecret;
		draftValue.ValueJson = ProtectDraftValueJson(normalizedValueJson, draftValue.IsSecret, draftValueProtector);
		draftValue.ChangeNote = NormalizeOptionalTextOrDefault(request.ChangeNote, "Draft value updated.");
		draftValue.UpdatedByUserId = actorUserId;
		draftValue.UpdatedAtUtc = now;

		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "draftValue.updated",
					targetType: "DraftValue",
					targetIdentifier: draftValue.Id.ToString(),
					targetDisplayName: $"{draftValue.ScopeType}:{draftValue.ScopeId}",
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["configItemId"] = draftValue.ConfigItemId,
						["scopeType"] = draftValue.ScopeType.ToString(),
						["scopeId"] = draftValue.ScopeId,
						["previousChangeNote"] = previousChangeNote,
						["isSecret"] = draftValue.IsSecret
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.Ok(ToDraftValueResponse(draftValue));
	})
	.RequirePermission(PermissionCatalog.ConfigWriteDraft, InstallationScopePath());

app.MapDelete(
	"/api/v1/draft-values/{draftValueId:guid}",
	async (
		Guid draftValueId,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var draftValue = await dbContext.DraftValues.FirstOrDefaultAsync(x => x.Id == draftValueId, cancellationToken);
		if (draftValue is null)
		{
			return Results.Problem(
				title: "Draft value not found",
				detail: $"Draft value '{draftValueId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var now = DateTimeOffset.UtcNow;
		var (actorUserId, actorUsername) = GetAuditActor(user);

		dbContext.DraftValues.Remove(draftValue);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "draftValue.deleted",
					targetType: "DraftValue",
					targetIdentifier: draftValue.Id.ToString(),
					targetDisplayName: $"{draftValue.ScopeType}:{draftValue.ScopeId}",
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["configItemId"] = draftValue.ConfigItemId,
						["scopeType"] = draftValue.ScopeType.ToString(),
						["scopeId"] = draftValue.ScopeId,
						["isSecret"] = draftValue.IsSecret
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);
		return Results.NoContent();
	})
	.RequirePermission(PermissionCatalog.ConfigDeleteDraft, InstallationScopePath());

	app.MapGet(
		"/api/v1/effective-snapshots/preview",
		async (
			Guid? applicationId,
			Guid? environmentId,
			Guid? managedNodeId,
			Guid? namespaceId,
			ClaimsPrincipal user,
			SecretManagerDbContext dbContext,
			IPermissionEvaluator permissionEvaluator,
			IDraftValueProtector draftValueProtector,
			CancellationToken cancellationToken) =>
		{
			var validationErrors = new Dictionary<string, string[]>();
			if (!applicationId.HasValue || applicationId.Value == Guid.Empty)
			{
				validationErrors[nameof(ApplicationDefinition.Id)] = ["ApplicationId is required."];
			}

			if (!environmentId.HasValue || environmentId.Value == Guid.Empty)
			{
				validationErrors[nameof(EnvironmentDefinition.Id)] = ["EnvironmentId is required."];
			}

			if (!managedNodeId.HasValue || managedNodeId.Value == Guid.Empty)
			{
				validationErrors[nameof(ManagedNodeRecord.Id)] = ["ManagedNodeId is required."];
			}

			if (namespaceId == Guid.Empty)
			{
				validationErrors[nameof(NamespaceDefinition.Id)] = ["NamespaceId must be omitted or a valid GUID."];
			}

			if (validationErrors.Count > 0)
			{
				return Results.ValidationProblem(validationErrors);
			}

			var previewApplicationId = applicationId!.Value;
			var previewEnvironmentId = environmentId!.Value;
			var previewManagedNodeId = managedNodeId!.Value;

			if (!await dbContext.Applications.AsNoTracking().AnyAsync(x => x.Id == previewApplicationId, cancellationToken))
			{
				return Results.Problem(
					title: "Application not found",
					detail: $"Application '{applicationId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			if (!await dbContext.Environments.AsNoTracking().AnyAsync(x => x.Id == previewEnvironmentId, cancellationToken))
			{
				return Results.Problem(
					title: "Environment not found",
					detail: $"Environment '{environmentId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			var managedNode = await dbContext.ManagedNodes
				.AsNoTracking()
			.Where(x => x.Id == previewManagedNodeId)
				.Select(x => new { x.Id, x.EnvironmentId, x.NodeGroupId })
				.FirstOrDefaultAsync(cancellationToken);

			if (managedNode is null)
			{
				return Results.Problem(
					title: "Managed node not found",
					detail: $"Managed node '{managedNodeId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			if (managedNode.EnvironmentId != previewEnvironmentId)
			{
				return Results.Problem(
					title: "Managed node mismatch",
					detail: $"Managed node '{managedNodeId}' does not belong to environment '{environmentId}'.",
					statusCode: StatusCodes.Status404NotFound);
			}

			if (namespaceId.HasValue && !await dbContext.Namespaces.AsNoTracking().AnyAsync(
				x => x.Id == namespaceId.Value && x.ApplicationId == previewApplicationId,
					cancellationToken))
			{
				return Results.Problem(
					title: "Namespace not found",
					detail: $"Namespace '{namespaceId}' does not exist for application '{applicationId}'.",
					statusCode: StatusCodes.Status404NotFound);
			}

			var assignmentExists = await dbContext.ApplicationAssignments.AsNoTracking().AnyAsync(
			x => x.ApplicationId == previewApplicationId
				&& x.EnvironmentId == previewEnvironmentId
					&& x.Enabled
				&& (x.ManagedNodeId == previewManagedNodeId
						|| (managedNode.NodeGroupId.HasValue && x.NodeGroupId == managedNode.NodeGroupId.Value)),
				cancellationToken);

			if (!assignmentExists)
			{
				return Results.Problem(
					title: "Application assignment not found",
					detail: $"Application '{applicationId}' is not assigned to managed node '{managedNodeId}' in environment '{environmentId}'.",
					statusCode: StatusCodes.Status404NotFound);
			}

			var effectivePreviewCandidates = await (
				from draftValue in dbContext.DraftValues.AsNoTracking()
				join configItem in dbContext.ConfigItems.AsNoTracking() on draftValue.ConfigItemId equals configItem.Id
				where configItem.ApplicationId == previewApplicationId
					&& (!namespaceId.HasValue || configItem.NamespaceId == namespaceId.Value)
				select new
				{
					draftValue.Id,
					draftValue.ConfigItemId,
					draftValue.ScopeType,
					draftValue.ScopeId,
					draftValue.ValueJson,
					draftValue.IsSecret,
					draftValue.UpdatedAtUtc,
					configItem.FullPath,
					configItem.ValueType
				})
				.ToListAsync(cancellationToken);

			var resolvedItems = EffectivePreviewResolver.Resolve(
			new EffectivePreviewTarget(previewApplicationId, previewEnvironmentId, managedNode.NodeGroupId, previewManagedNodeId),
				effectivePreviewCandidates.Select(x => new EffectivePreviewCandidate(
					x.Id,
					x.ConfigItemId,
					x.FullPath,
					x.ValueType,
					x.ValueJson,
					x.IsSecret,
					x.ScopeType,
					x.ScopeId,
					x.UpdatedAtUtc)));

			var canRevealSecrets = await UserHasPermissionAsync(
				user,
				PermissionCatalog.ConfigRevealSecret,
				permissionEvaluator,
				cancellationToken);

			return Results.Ok(
				ToEffectivePreviewResponse(
					previewApplicationId,
					previewEnvironmentId,
					previewManagedNodeId,
					managedNode.NodeGroupId,
					resolvedItems,
					canRevealSecrets,
					draftValueProtector));
		})
		.RequirePermission(PermissionCatalog.ConfigReadMasked, InstallationScopePath());

	app.MapPost(
		"/api/v1/nodes/{nodeId:guid}/enrollment-token",
		async (
			Guid nodeId,
			ClaimsPrincipal user,
			HttpContext httpContext,
			SecretManagerDbContext dbContext,
			IAuditEventWriter auditEventWriter,
			Argon2PasswordHasher passwordHasher,
			CancellationToken cancellationToken) =>
		{
			var managedNode = await dbContext.ManagedNodes.FirstOrDefaultAsync(x => x.Id == nodeId, cancellationToken);
			if (managedNode is null)
			{
				return Results.Problem(
					title: "Managed node not found",
					detail: $"Managed node '{nodeId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			if (await dbContext.AgentRegistrations.AsNoTracking().AnyAsync(x => x.ManagedNodeId == nodeId, cancellationToken))
			{
				return Results.Conflict();
			}

			var now = DateTimeOffset.UtcNow;
			var enrollmentTokenValue = GenerateOpaqueSecret();
			var (actorUserId, actorUsername) = GetAuditActor(user);
			var enrollmentToken = new AgentEnrollmentToken
			{
				Id = Guid.NewGuid(),
				ManagedNodeId = managedNode.Id,
				TokenHash = passwordHasher.Hash(enrollmentTokenValue),
				IssuedByUserId = actorUserId,
				CreatedAtUtc = now,
				ExpiresAtUtc = now.AddMinutes(15)
			};

			dbContext.AgentEnrollmentTokens.Add(enrollmentToken);
			dbContext.AuditEvents.Add(
				auditEventWriter.Create(
					CreateAuditRequest(
						httpContext,
						action: "agentEnrollmentToken.created",
						targetType: "ManagedNode",
						targetIdentifier: managedNode.Id.ToString(),
						targetDisplayName: managedNode.Hostname,
						outcome: "Succeeded",
						actorUserId: actorUserId,
						actorUsername: actorUsername,
						details: new Dictionary<string, object?>
						{
							["managedNodeId"] = managedNode.Id,
							["environmentId"] = managedNode.EnvironmentId,
							["enrollmentTokenId"] = enrollmentToken.Id,
							["expiresAtUtc"] = enrollmentToken.ExpiresAtUtc
						},
						installationId: Installation.SingletonId,
						occurredAtUtc: now)));

			await dbContext.SaveChangesAsync(cancellationToken);
			return Results.Ok(new IssueAgentEnrollmentTokenResponse(
				enrollmentToken.Id,
				managedNode.Id,
				enrollmentToken.ExpiresAtUtc,
				enrollmentTokenValue));
		})
		.RequirePermission(PermissionCatalog.AgentsWrite, InstallationScopePath());

	app.MapPost(
		"/api/v1/agent/enroll",
		async (
			AgentEnrollRequest request,
			HttpContext httpContext,
			SecretManagerDbContext dbContext,
			IAuditEventWriter auditEventWriter,
			Argon2PasswordHasher passwordHasher,
			CancellationToken cancellationToken) =>
		{
			var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
			if (request.ManagedNodeId == Guid.Empty)
			{
				validationErrors[nameof(request.ManagedNodeId)] = ["ManagedNodeId is required."];
			}

			if (validationErrors.Count > 0)
			{
				return Results.ValidationProblem(validationErrors);
			}

			var managedNode = await dbContext.ManagedNodes.FirstOrDefaultAsync(x => x.Id == request.ManagedNodeId, cancellationToken);
			if (managedNode is null)
			{
				return Results.Problem(
					title: "Managed node not found",
					detail: $"Managed node '{request.ManagedNodeId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			if (!string.Equals(NormalizeHostname(request.Hostname), NormalizeHostname(managedNode.Hostname), StringComparison.Ordinal))
			{
				return Results.Problem(
					title: "Managed node mismatch",
					detail: $"Hostname '{request.Hostname}' does not match the managed node record.",
					statusCode: StatusCodes.Status409Conflict);
			}

			if (await dbContext.AgentRegistrations.AsNoTracking().AnyAsync(x => x.ManagedNodeId == request.ManagedNodeId, cancellationToken))
			{
				return Results.Conflict();
			}

			var matchingToken = await FindMatchingEnrollmentTokenAsync(
				request.ManagedNodeId,
				request.EnrollmentToken,
				dbContext,
				passwordHasher,
				cancellationToken);
			if (matchingToken is null)
			{
				return Results.Forbid();
			}

			var now = DateTimeOffset.UtcNow;
			if (matchingToken.ConsumedAtUtc.HasValue)
			{
				return Results.Conflict();
			}

			if (matchingToken.ExpiresAtUtc <= now)
			{
				return Results.Forbid();
			}

			var agentId = Guid.NewGuid();
			var agentCredential = GenerateOpaqueSecret();
			var enrollmentSecret = GenerateOpaqueSecret();
			var agentRegistration = new AgentRegistration
			{
				Id = agentId,
				ManagedNodeId = managedNode.Id,
				CredentialHash = passwordHasher.Hash(agentCredential),
				HealthStatus = "Pending",
				EnrolledAtUtc = now,
				CreatedAtUtc = now,
				UpdatedAtUtc = now
			};

			matchingToken.ConsumedAtUtc = now;
			matchingToken.ConsumedByAgentId = agentRegistration.Id;
			managedNode.Platform = NormalizeCatalogKeyword(request.Platform, managedNode.Platform.Length == 0 ? "unknown" : managedNode.Platform);
			managedNode.AgentVersion = NormalizeOptionalTextOrDefault(request.AgentVersion, managedNode.AgentVersion.Length == 0 ? "unknown" : managedNode.AgentVersion);
			managedNode.Status = "Enrolled";
			managedNode.UpdatedAtUtc = now;

			var initialAssignments = await BuildInitialAgentAssignmentsAsync(managedNode, dbContext, cancellationToken);
			dbContext.AgentRegistrations.Add(agentRegistration);
			dbContext.AuditEvents.Add(
				auditEventWriter.Create(
					CreateAuditRequest(
						httpContext,
						action: "agent.enrolled",
						targetType: "AgentRegistration",
						targetIdentifier: agentRegistration.Id.ToString(),
						targetDisplayName: managedNode.Hostname,
						outcome: "Succeeded",
						actorUsername: "agent-enroll",
						details: new Dictionary<string, object?>
						{
							["managedNodeId"] = managedNode.Id,
							["environmentId"] = managedNode.EnvironmentId,
							["agentId"] = agentRegistration.Id,
							["initialAssignmentCount"] = initialAssignments.Count
						},
						installationId: Installation.SingletonId,
						occurredAtUtc: now)));

			await dbContext.SaveChangesAsync(cancellationToken);
			return Results.Ok(new AgentEnrollmentResponse(
				agentRegistration.Id,
				managedNode.Id,
				managedNode.EnvironmentId,
				agentCredential,
				enrollmentSecret,
				agentRegistration.EnrolledAtUtc,
				initialAssignments));
		});

	app.MapPost(
		"/api/v1/agent/heartbeat",
		async (
			AgentHeartbeatRequest request,
			SecretManagerDbContext dbContext,
			Argon2PasswordHasher passwordHasher,
			CancellationToken cancellationToken) =>
		{
			var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
			if (request.AgentId == Guid.Empty)
			{
				validationErrors[nameof(request.AgentId)] = ["AgentId is required."];
			}

			if (validationErrors.Count > 0)
			{
				return Results.ValidationProblem(validationErrors);
			}

			var (agentRegistration, managedNode) = await AuthenticateAgentAsync(
				request.AgentId,
				request.AgentCredential,
				dbContext,
				passwordHasher,
				cancellationToken);
			if (agentRegistration is null || managedNode is null)
			{
				return Results.Forbid();
			}

			if (request.CurrentPublishedVersionId.HasValue && !await dbContext.PublishedVersions.AsNoTracking().AnyAsync(
					x => x.Id == request.CurrentPublishedVersionId.Value && x.EnvironmentId == managedNode.EnvironmentId,
					cancellationToken))
			{
				return Results.Problem(
					title: "Published version not found",
					detail: $"Published version '{request.CurrentPublishedVersionId}' does not exist for the enrolled environment.",
					statusCode: StatusCodes.Status404NotFound);
			}

			var now = DateTimeOffset.UtcNow;
			agentRegistration.LastSeenAtUtc = now;
			agentRegistration.CurrentPublishedVersionId = request.CurrentPublishedVersionId;
			agentRegistration.CurrentVersionNumber = request.CurrentVersionNumber;
			agentRegistration.HealthStatus = "Online";
			agentRegistration.UpdatedAtUtc = now;

			managedNode.LastSeenAtUtc = now;
			managedNode.AgentVersion = NormalizeOptionalTextOrDefault(request.AgentVersion, managedNode.AgentVersion.Length == 0 ? "unknown" : managedNode.AgentVersion);
			managedNode.Status = "Online";
			managedNode.UpdatedAtUtc = now;

			await dbContext.SaveChangesAsync(cancellationToken);
			return Results.Ok(ToAgentStatusResponse(agentRegistration, managedNode));
		});

	app.MapPost(
		"/api/v1/agent/sync/check",
		async (
			AgentSyncCheckRequest request,
			SecretManagerDbContext dbContext,
			Argon2PasswordHasher passwordHasher,
			IDraftValueProtector draftValueProtector,
			IAgentSnapshotCache snapshotCache,
			CancellationToken cancellationToken) =>
		{
			var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
			if (request.AgentId == Guid.Empty)
			{
				validationErrors[nameof(request.AgentId)] = ["AgentId is required."];
			}

			if (validationErrors.Count > 0)
			{
				return Results.ValidationProblem(validationErrors);
			}

			var (agentRegistration, managedNode) = await AuthenticateAgentAsync(
				request.AgentId,
				request.AgentCredential,
				dbContext,
				passwordHasher,
				cancellationToken);
			if (agentRegistration is null || managedNode is null)
			{
				return Results.Forbid();
			}

			var initialAssignments = await BuildInitialAgentAssignmentsAsync(managedNode, dbContext, cancellationToken);
			var publishedVersionIds = initialAssignments
				.Where(x => x.PublishedVersionId.HasValue)
				.Select(x => x.PublishedVersionId!.Value)
				.ToList();

			if (publishedVersionIds.Count == 0)
			{
				return Results.Ok(new AgentSyncCheckResponse(agentRegistration.Id, DateTimeOffset.UtcNow, []));
			}

			var publishedVersions = await (
				from publishedVersion in dbContext.PublishedVersions.AsNoTracking()
				join application in dbContext.Applications.AsNoTracking() on publishedVersion.ApplicationId equals application.Id
				where publishedVersionIds.Contains(publishedVersion.Id)
				select new
				{
					PublishedVersion = publishedVersion,
					ApplicationSlug = application.Slug
				})
				.ToListAsync(cancellationToken);

			var snapshots = new List<AgentSyncSnapshotReferenceResponse>();
			foreach (var publishedVersion in publishedVersions.OrderBy(x => x.PublishedVersion.ApplicationId))
			{
				var snapshot = BuildCanonicalAgentSnapshotResponse(managedNode, publishedVersion.PublishedVersion, draftValueProtector);
				try
				{
					await snapshotCache.SetAsync(snapshot.SnapshotId, JsonSerializer.Serialize(snapshot), cancellationToken);
				}
				catch
				{
					// Canonical fetch remains available when Redis is unavailable.
				}

				snapshots.Add(new AgentSyncSnapshotReferenceResponse(
					snapshot.SnapshotId,
					snapshot.ApplicationId,
					publishedVersion.ApplicationSlug,
					snapshot.PublishedVersionId,
					snapshot.VersionNumber,
					snapshot.SnapshotHash,
					snapshot.RolloutPolicy));
			}

			return Results.Ok(new AgentSyncCheckResponse(agentRegistration.Id, DateTimeOffset.UtcNow, snapshots));
		});

	app.MapGet(
		"/api/v1/agent/snapshots/{snapshotId}",
		async (
			string snapshotId,
			Guid agentId,
			string agentCredential,
			SecretManagerDbContext dbContext,
			Argon2PasswordHasher passwordHasher,
			IDraftValueProtector draftValueProtector,
			IAgentSnapshotCache snapshotCache,
			CancellationToken cancellationToken) =>
		{
			if (agentId == Guid.Empty || string.IsNullOrWhiteSpace(agentCredential))
			{
				return Results.ValidationProblem(new Dictionary<string, string[]>
				{
					[nameof(agentId)] = ["AgentId and AgentCredential are required."]
				});
			}

			var (agentRegistration, managedNode) = await AuthenticateAgentAsync(
				agentId,
				agentCredential,
				dbContext,
				passwordHasher,
				cancellationToken);
			if (agentRegistration is null || managedNode is null)
			{
				return Results.Forbid();
			}

			if (!TryParseSnapshotId(snapshotId, out var publishedVersionId, out var managedNodeId, out var applicationId)
				|| managedNodeId != managedNode.Id)
			{
				return Results.Problem(
					title: "Snapshot not found",
					detail: $"Snapshot '{snapshotId}' does not exist for the authenticated agent.",
					statusCode: StatusCodes.Status404NotFound);
			}

			AgentSnapshotResponse? snapshot = null;
			try
			{
				var cachedPayload = await snapshotCache.GetAsync(snapshotId, cancellationToken);
				if (!string.IsNullOrWhiteSpace(cachedPayload))
				{
					var cachedSnapshot = JsonSerializer.Deserialize<AgentSnapshotResponse>(cachedPayload);
					if (cachedSnapshot is not null
						&& cachedSnapshot.ManagedNodeId == managedNode.Id
						&& cachedSnapshot.ApplicationId == applicationId
						&& cachedSnapshot.PublishedVersionId == publishedVersionId
						&& ValidateAgentSnapshotHash(cachedSnapshot))
					{
						snapshot = cachedSnapshot with { Source = "redis" };
					}
				}
			}
			catch
			{
				// Canonical fallback remains available when Redis is unavailable.
			}

			if (snapshot is null)
			{
				var publishedVersion = await dbContext.PublishedVersions
					.AsNoTracking()
					.FirstOrDefaultAsync(
						x => x.Id == publishedVersionId && x.ApplicationId == applicationId && x.EnvironmentId == managedNode.EnvironmentId,
						cancellationToken);
				if (publishedVersion is null)
				{
					return Results.Problem(
						title: "Published version not found",
						detail: $"Published version '{publishedVersionId}' does not exist for the requested snapshot.",
						statusCode: StatusCodes.Status404NotFound);
				}

				snapshot = BuildCanonicalAgentSnapshotResponse(managedNode, publishedVersion, draftValueProtector) with { Source = "canonical" };
			}

			return Results.Ok(snapshot);
		});

	app.MapGet(
		"/api/v1/agent/notifications/stream",
		async (
			Guid agentId,
			string agentCredential,
			HttpContext httpContext,
			SecretManagerDbContext dbContext,
			Argon2PasswordHasher passwordHasher,
			IAgentInvalidationHub invalidationHub,
			CancellationToken cancellationToken) =>
		{
			if (agentId == Guid.Empty || string.IsNullOrWhiteSpace(agentCredential))
			{
				return Results.ValidationProblem(new Dictionary<string, string[]>
				{
					[nameof(agentId)] = ["AgentId and AgentCredential are required."]
				});
			}

			var (agentRegistration, _) = await AuthenticateAgentAsync(
				agentId,
				agentCredential,
				dbContext,
				passwordHasher,
				cancellationToken);
			if (agentRegistration is null)
			{
				return Results.Forbid();
			}

			httpContext.Response.Headers.CacheControl = "no-cache";
			httpContext.Response.Headers.ContentType = "text/event-stream";
			await foreach (var notification in invalidationHub.SubscribeAsync(agentRegistration.Id, cancellationToken))
			{
				await httpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(notification)}\n\n", cancellationToken);
				await httpContext.Response.Body.FlushAsync(cancellationToken);
			}

			return Results.Empty;
		});

	app.MapGet(
		"/api/v1/agents",
		async (
			Guid? environmentId,
			SecretManagerDbContext dbContext,
			CancellationToken cancellationToken) =>
		{
			var query = from agentRegistration in dbContext.AgentRegistrations.AsNoTracking()
				join managedNode in dbContext.ManagedNodes.AsNoTracking() on agentRegistration.ManagedNodeId equals managedNode.Id
				select new { AgentRegistration = agentRegistration, ManagedNode = managedNode };

			if (environmentId.HasValue)
			{
				query = query.Where(x => x.ManagedNode.EnvironmentId == environmentId.Value);
			}

			var agents = await query.ToListAsync(cancellationToken);
			return Results.Ok(agents
				.OrderBy(x => x.ManagedNode.Hostname, StringComparer.OrdinalIgnoreCase)
				.Select(x => ToAgentStatusResponse(x.AgentRegistration, x.ManagedNode))
				.ToList());
		})
		.RequirePermission(PermissionCatalog.AgentsRead, InstallationScopePath());

	app.MapGet(
		"/api/v1/agents/{agentId:guid}/status",
		async (
			Guid agentId,
			SecretManagerDbContext dbContext,
			CancellationToken cancellationToken) =>
		{
			var status = await (
				from agentRegistration in dbContext.AgentRegistrations.AsNoTracking()
				join managedNode in dbContext.ManagedNodes.AsNoTracking() on agentRegistration.ManagedNodeId equals managedNode.Id
				where agentRegistration.Id == agentId
				select new { AgentRegistration = agentRegistration, ManagedNode = managedNode })
				.FirstOrDefaultAsync(cancellationToken);

			if (status is null)
			{
				return Results.Problem(
					title: "Agent not found",
					detail: $"Agent '{agentId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			return Results.Ok(ToAgentStatusResponse(status.AgentRegistration, status.ManagedNode));
		})
		.RequirePermission(PermissionCatalog.AgentsRead, InstallationScopePath());

	app.MapGet(
		"/api/v1/published-versions",
		async (
			Guid? applicationId,
			Guid? environmentId,
			SecretManagerDbContext dbContext,
			CancellationToken cancellationToken) =>
		{
			var query = dbContext.PublishedVersions.AsNoTracking();
			if (applicationId.HasValue)
			{
				query = query.Where(x => x.ApplicationId == applicationId.Value);
			}

			if (environmentId.HasValue)
			{
				query = query.Where(x => x.EnvironmentId == environmentId.Value);
			}

			var publishedVersions = await query
				.OrderByDescending(x => x.VersionNumber)
				.ThenByDescending(x => x.PublishedAtUtc)
				.Select(x => new PublishedVersionResponse(
					x.Id,
					x.PublishOperationId,
					x.EnvironmentId,
					x.ApplicationId,
					x.VersionNumber,
					x.RolloutPolicy,
					x.ContentHash,
					x.PublishedByUserId,
					x.PublishedAtUtc,
					x.SupersedesVersionId))
				.ToListAsync(cancellationToken);

			return Results.Ok(publishedVersions);
		})
		.RequirePermission(PermissionCatalog.ConfigReadMasked, InstallationScopePath());

	app.MapPost(
		"/api/v1/publishes",
		async (
			CreatePublishRequest request,
			ClaimsPrincipal user,
			HttpContext httpContext,
			SecretManagerDbContext dbContext,
			IAgentInvalidationHub invalidationHub,
			IAuditEventWriter auditEventWriter,
			CancellationToken cancellationToken) =>
		{
			var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
			if (request.ApplicationId == Guid.Empty)
			{
				validationErrors[nameof(request.ApplicationId)] = ["ApplicationId is required."];
			}

			if (request.EnvironmentId == Guid.Empty)
			{
				validationErrors[nameof(request.EnvironmentId)] = ["EnvironmentId is required."];
			}

			var normalizedRolloutPolicy = NormalizeCatalogKeyword(request.RolloutPolicy, "immediate");
			if (validationErrors.Count > 0)
			{
				return Results.ValidationProblem(validationErrors);
			}

			if (!await dbContext.Applications.AsNoTracking().AnyAsync(x => x.Id == request.ApplicationId, cancellationToken))
			{
				return Results.Problem(
					title: "Application not found",
					detail: $"Application '{request.ApplicationId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			if (!await dbContext.Environments.AsNoTracking().AnyAsync(x => x.Id == request.EnvironmentId, cancellationToken))
			{
				return Results.Problem(
					title: "Environment not found",
					detail: $"Environment '{request.EnvironmentId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			var environmentNodeGroupIds = (await dbContext.NodeGroups
				.AsNoTracking()
				.Where(x => x.EnvironmentId == request.EnvironmentId)
				.Select(x => x.Id)
				.ToListAsync(cancellationToken))
				.ToHashSet();

			var environmentManagedNodeIds = (await dbContext.ManagedNodes
				.AsNoTracking()
				.Where(x => x.EnvironmentId == request.EnvironmentId)
				.Select(x => x.Id)
				.ToListAsync(cancellationToken))
				.ToHashSet();

			var publishDraftRows = await (
				from draftValue in dbContext.DraftValues.AsNoTracking()
				join configItem in dbContext.ConfigItems.AsNoTracking() on draftValue.ConfigItemId equals configItem.Id
				where configItem.ApplicationId == request.ApplicationId
				select new
				{
					draftValue.Id,
					draftValue.ConfigItemId,
					draftValue.ScopeType,
					draftValue.ScopeId,
					draftValue.ValueJson,
					draftValue.IsSecret,
					draftValue.ChangeNote,
					draftValue.UpdatedAtUtc,
					configItem.FullPath,
					configItem.ValueType
				})
				.ToListAsync(cancellationToken);

			var publishedDraftValues = publishDraftRows
				.Where(x => IsPublishScopeRelevant(
					x.ScopeType,
					x.ScopeId,
					request.ApplicationId,
					request.EnvironmentId,
					environmentNodeGroupIds,
					environmentManagedNodeIds))
				.Select(x => new PublishedVersionPayloadItem(
					x.Id,
					x.ConfigItemId,
					x.FullPath,
					x.ValueType,
					x.IsSecret,
					x.ScopeType.ToString(),
					x.ScopeId,
					x.ValueJson,
					x.ChangeNote,
					x.UpdatedAtUtc))
				.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => GetDraftScopeOrderFromName(x.ScopeType))
				.ThenBy(x => x.ScopeId)
				.ThenBy(x => x.ConfigItemId)
				.ToList();

			var payloadJson = BuildPublishedVersionPayloadJson(
				request.EnvironmentId,
				request.ApplicationId,
				normalizedRolloutPolicy,
				publishedDraftValues);
			var (actorUserId, actorUsername) = GetAuditActor(user);
			var (publishOperation, publishedVersion) = await CreateImmutablePublishedVersionAsync(
				request.EnvironmentId,
				request.ApplicationId,
				NormalizeOptionalTextOrDefault(request.ChangeSummary, "Publish requested."),
				normalizedRolloutPolicy,
				payloadJson,
				actorUserId,
				dbContext,
				cancellationToken);

			dbContext.PublishOperations.Add(publishOperation);
			dbContext.PublishedVersions.Add(publishedVersion);
			dbContext.AuditEvents.Add(
				auditEventWriter.Create(
					CreateAuditRequest(
						httpContext,
						action: "publish.created",
						targetType: "PublishOperation",
						targetIdentifier: publishOperation.Id.ToString(),
						targetDisplayName: $"{request.EnvironmentId}:{request.ApplicationId}:v{publishedVersion.VersionNumber}",
						outcome: "Succeeded",
						actorUserId: actorUserId,
						actorUsername: actorUsername,
						details: new Dictionary<string, object?>
						{
							["environmentId"] = request.EnvironmentId,
							["applicationId"] = request.ApplicationId,
							["publishedVersionId"] = publishedVersion.Id,
							["versionNumber"] = publishedVersion.VersionNumber,
							["contentHash"] = publishedVersion.ContentHash,
							["draftValueCount"] = publishedDraftValues.Count,
							["rolloutPolicy"] = publishedVersion.RolloutPolicy
						},
						installationId: Installation.SingletonId,
						occurredAtUtc: publishedVersion.PublishedAtUtc)));

			await dbContext.SaveChangesAsync(cancellationToken);
			var targetAgentIds = await ResolveTargetAgentIdsAsync(
				publishedVersion.EnvironmentId,
				publishedVersion.ApplicationId,
				dbContext,
				cancellationToken);
			foreach (var targetAgentId in targetAgentIds)
			{
				await invalidationHub.PublishAsync(
					[targetAgentId],
					new AgentInvalidationNotification(
						targetAgentId,
						publishedVersion.ApplicationId,
						publishedVersion.Id,
						publishedVersion.VersionNumber,
						publishedVersion.PublishedAtUtc),
					cancellationToken);
			}
			return Results.Ok(new CreatePublishResponse(
				ToPublishOperationResponse(publishOperation),
				ToPublishedVersionResponse(publishedVersion)));
		})
		.RequirePermission(PermissionCatalog.ConfigPublish, InstallationScopePath());

	app.MapGet(
		"/api/v1/published-versions/{versionId:guid}/diff",
		async (
			Guid versionId,
			Guid? compareToVersionId,
			ClaimsPrincipal user,
			SecretManagerDbContext dbContext,
			IPermissionEvaluator permissionEvaluator,
			IDraftValueProtector draftValueProtector,
			CancellationToken cancellationToken) =>
		{
			var version = await dbContext.PublishedVersions
				.AsNoTracking()
				.FirstOrDefaultAsync(x => x.Id == versionId, cancellationToken);
			if (version is null)
			{
				return Results.Problem(
					title: "Published version not found",
					detail: $"Published version '{versionId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			var resolvedCompareToVersionId = compareToVersionId ?? version.SupersedesVersionId;
			if (!resolvedCompareToVersionId.HasValue)
			{
				return Results.ValidationProblem(new Dictionary<string, string[]>
				{
					[nameof(compareToVersionId)] = ["CompareToVersionId is required when the selected version does not supersede another version."]
				});
			}

			var compareToVersion = await dbContext.PublishedVersions
				.AsNoTracking()
				.FirstOrDefaultAsync(x => x.Id == resolvedCompareToVersionId.Value, cancellationToken);
			if (compareToVersion is null)
			{
				return Results.Problem(
					title: "Comparison version not found",
					detail: $"Published version '{resolvedCompareToVersionId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			if (compareToVersion.ApplicationId != version.ApplicationId || compareToVersion.EnvironmentId != version.EnvironmentId)
			{
				return Results.ValidationProblem(new Dictionary<string, string[]>
				{
					[nameof(compareToVersionId)] = ["Versions must belong to the same environment and application."]
				});
			}

			var currentPayload = DeserializePublishedVersionPayload(version.PayloadJson);
			var previousPayload = DeserializePublishedVersionPayload(compareToVersion.PayloadJson);
			var canRevealSecrets = await UserHasPermissionAsync(
				user,
				PermissionCatalog.ConfigRevealSecret,
				permissionEvaluator,
				cancellationToken);

			var currentItemsByKey = currentPayload.DraftValues.ToDictionary(x => BuildPublishedVersionItemKey(x), StringComparer.Ordinal);
			var previousItemsByKey = previousPayload.DraftValues.ToDictionary(x => BuildPublishedVersionItemKey(x), StringComparer.Ordinal);
			var changeKeys = currentItemsByKey.Keys
				.Union(previousItemsByKey.Keys, StringComparer.Ordinal)
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToList();

			var changes = new List<PublishedVersionDiffItemResponse>();
			foreach (var changeKey in changeKeys)
			{
				currentItemsByKey.TryGetValue(changeKey, out var currentItem);
				previousItemsByKey.TryGetValue(changeKey, out var previousItem);

				if (currentItem is not null && previousItem is not null && PublishedVersionItemsEqual(previousItem, currentItem))
				{
					continue;
				}

				var exemplar = currentItem ?? previousItem!;
				var isSecret = exemplar.IsSecret;
				changes.Add(new PublishedVersionDiffItemResponse(
					previousItem?.DraftValueId,
					currentItem?.DraftValueId,
					exemplar.ConfigItemId,
					exemplar.FullPath,
					exemplar.ScopeType,
					exemplar.ScopeId,
					currentItem is null
						? "Removed"
						: previousItem is null
							? "Added"
							: "Modified",
					FormatPublishedVersionValueJson(previousItem?.ValueJson, isSecret, canRevealSecrets, draftValueProtector),
					FormatPublishedVersionValueJson(currentItem?.ValueJson, isSecret, canRevealSecrets, draftValueProtector),
					isSecret,
					isSecret && !canRevealSecrets));
			}

			return Results.Ok(new PublishedVersionDiffResponse(
				version.Id,
				compareToVersion.Id,
				version.RolloutPolicy,
				compareToVersion.RolloutPolicy,
				!string.Equals(version.RolloutPolicy, compareToVersion.RolloutPolicy, StringComparison.Ordinal),
				changes.Count,
				changes));
		})
		.RequirePermission(PermissionCatalog.ConfigReadMasked, InstallationScopePath());

	app.MapPost(
		"/api/v1/published-versions/{versionId:guid}/rollback",
		async (
			Guid versionId,
			CreateRollbackRequest request,
			ClaimsPrincipal user,
			HttpContext httpContext,
			SecretManagerDbContext dbContext,
			IAgentInvalidationHub invalidationHub,
			IAuditEventWriter auditEventWriter,
			CancellationToken cancellationToken) =>
		{
			var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
			if (validationErrors.Count > 0)
			{
				return Results.ValidationProblem(validationErrors);
			}

			var sourceVersion = await dbContext.PublishedVersions
				.AsNoTracking()
				.FirstOrDefaultAsync(x => x.Id == versionId, cancellationToken);
			if (sourceVersion is null)
			{
				return Results.Problem(
					title: "Published version not found",
					detail: $"Published version '{versionId}' does not exist.",
					statusCode: StatusCodes.Status404NotFound);
			}

			var (actorUserId, actorUsername) = GetAuditActor(user);
			var rollbackSummary = NormalizeOptionalTextOrDefault(
				request.ChangeSummary,
				$"Rollback to version {sourceVersion.VersionNumber}.");
			var (publishOperation, publishedVersion) = await CreateImmutablePublishedVersionAsync(
				sourceVersion.EnvironmentId,
				sourceVersion.ApplicationId,
				rollbackSummary,
				sourceVersion.RolloutPolicy,
				sourceVersion.PayloadJson,
				actorUserId,
				dbContext,
				cancellationToken);

			dbContext.PublishOperations.Add(publishOperation);
			dbContext.PublishedVersions.Add(publishedVersion);
			dbContext.AuditEvents.Add(
				auditEventWriter.Create(
					CreateAuditRequest(
						httpContext,
						action: "rollback.created",
						targetType: "PublishedVersion",
						targetIdentifier: publishedVersion.Id.ToString(),
						targetDisplayName: $"{sourceVersion.EnvironmentId}:{sourceVersion.ApplicationId}:v{publishedVersion.VersionNumber}",
						outcome: "Succeeded",
						actorUserId: actorUserId,
						actorUsername: actorUsername,
						details: new Dictionary<string, object?>
						{
							["sourcePublishedVersionId"] = sourceVersion.Id,
							["sourceVersionNumber"] = sourceVersion.VersionNumber,
							["environmentId"] = sourceVersion.EnvironmentId,
							["applicationId"] = sourceVersion.ApplicationId,
							["publishedVersionId"] = publishedVersion.Id,
							["versionNumber"] = publishedVersion.VersionNumber,
							["contentHash"] = publishedVersion.ContentHash
						},
						installationId: Installation.SingletonId,
						occurredAtUtc: publishedVersion.PublishedAtUtc)));

			await dbContext.SaveChangesAsync(cancellationToken);
			var targetAgentIds = await ResolveTargetAgentIdsAsync(
				publishedVersion.EnvironmentId,
				publishedVersion.ApplicationId,
				dbContext,
				cancellationToken);
			foreach (var targetAgentId in targetAgentIds)
			{
				await invalidationHub.PublishAsync(
					[targetAgentId],
					new AgentInvalidationNotification(
						targetAgentId,
						publishedVersion.ApplicationId,
						publishedVersion.Id,
						publishedVersion.VersionNumber,
						publishedVersion.PublishedAtUtc),
					cancellationToken);
			}
			return Results.Ok(new RollbackPublishedVersionResponse(
				sourceVersion.Id,
				ToPublishOperationResponse(publishOperation),
				ToPublishedVersionResponse(publishedVersion)));
		})
		.RequirePermission(PermissionCatalog.ConfigRollback, InstallationScopePath());

app.MapPost(
	"/api/v1/imports/appsettings/preview",
	async (
		AppSettingsImportPreviewRequest request,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.ApplicationId == Guid.Empty)
		{
			validationErrors[nameof(request.ApplicationId)] = ["ApplicationId is required."];
		}

		if (request.ScopeId == Guid.Empty)
		{
			validationErrors[nameof(request.ScopeId)] = ["ScopeId is required."];
		}

		if (!TryParseImportScopeType(request.ScopeType, out var scopeType))
		{
			validationErrors[nameof(request.ScopeType)] = ["ScopeType must be Installation, Environment, NodeGroup, or ManagedNode."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		if (!await dbContext.Applications.AsNoTracking().AnyAsync(x => x.Id == request.ApplicationId, cancellationToken))
		{
			return Results.Problem(
				title: "Application not found",
				detail: $"Application '{request.ApplicationId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var scopeValidationResult = await ValidateImportScopeAsync(scopeType, request.ScopeId, dbContext, cancellationToken);
		if (scopeValidationResult is not null)
		{
			return scopeValidationResult;
		}

		var (plan, importValidationErrors) = await BuildAppSettingsImportPlanAsync(
			request.ApplicationId,
			request.JsonPayload,
			request.SecretFullPaths,
			scopeType,
			request.ScopeId,
			dbContext,
			cancellationToken);

		if (importValidationErrors is not null)
		{
			return Results.ValidationProblem(importValidationErrors);
		}

		return Results.Ok(ToAppSettingsImportPreviewResponse(plan!));
	})
	.RequirePermission(PermissionCatalog.ConfigReadMasked, InstallationScopePath());

app.MapPost(
	"/api/v1/imports/appsettings/apply",
	async (
		AppSettingsImportApplyRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		IDraftValueProtector draftValueProtector,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();
		if (request.ApplicationId == Guid.Empty)
		{
			validationErrors[nameof(request.ApplicationId)] = ["ApplicationId is required."];
		}

		if (request.ScopeId == Guid.Empty)
		{
			validationErrors[nameof(request.ScopeId)] = ["ScopeId is required."];
		}

		if (!TryParseImportScopeType(request.ScopeType, out var scopeType))
		{
			validationErrors[nameof(request.ScopeType)] = ["ScopeType must be Installation, Environment, NodeGroup, or ManagedNode."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		if (!await dbContext.Applications.AsNoTracking().AnyAsync(x => x.Id == request.ApplicationId, cancellationToken))
		{
			return Results.Problem(
				title: "Application not found",
				detail: $"Application '{request.ApplicationId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var scopeValidationResult = await ValidateImportScopeAsync(scopeType, request.ScopeId, dbContext, cancellationToken);
		if (scopeValidationResult is not null)
		{
			return scopeValidationResult;
		}

		var (plan, importValidationErrors) = await BuildAppSettingsImportPlanAsync(
			request.ApplicationId,
			request.JsonPayload,
			request.SecretFullPaths,
			scopeType,
			request.ScopeId,
			dbContext,
			cancellationToken);

		if (importValidationErrors is not null)
		{
			return Results.ValidationProblem(importValidationErrors);
		}

		var now = DateTimeOffset.UtcNow;
		var changeNote = NormalizeOptionalTextOrDefault(request.ChangeNote, "Imported from appsettings.");
		var createdNamespaceCount = 0;
		var createdConfigItemCount = 0;
		var updatedConfigItemCount = 0;
		var createdDraftValueCount = 0;
		var updatedDraftValueCount = 0;

		var namespaceIdByPath = plan!.Namespaces
			.Where(x => x.ExistingNamespaceId.HasValue)
			.ToDictionary(x => x.Path, x => x.ExistingNamespaceId!.Value, StringComparer.OrdinalIgnoreCase);

		foreach (var namespacePlan in plan.Namespaces.Where(x => !x.ExistingNamespaceId.HasValue))
		{
			var catalogNamespace = new NamespaceDefinition
			{
				Id = Guid.NewGuid(),
				ApplicationId = request.ApplicationId,
				Name = namespacePlan.Name,
				Path = namespacePlan.Path,
				Description = "Imported from appsettings.",
				CreatedAtUtc = now,
				UpdatedAtUtc = now
			};

			dbContext.Namespaces.Add(catalogNamespace);
			namespaceIdByPath[namespacePlan.Path] = catalogNamespace.Id;
			createdNamespaceCount += 1;
		}

		var existingConfigItemIds = plan.ConfigItems
			.Where(x => x.ExistingConfigItemId.HasValue)
			.Select(x => x.ExistingConfigItemId!.Value)
			.Distinct()
			.ToList();

		var existingConfigItems = await dbContext.ConfigItems
			.Where(x => existingConfigItemIds.Contains(x.Id))
			.ToDictionaryAsync(x => x.Id, cancellationToken);

		var configItemIdByFullPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
		foreach (var configItem in existingConfigItems.Values)
		{
			configItemIdByFullPath[configItem.FullPath] = configItem.Id;
		}

		foreach (var itemPlan in plan.ConfigItems)
		{
			if (itemPlan.ExistingConfigItemId.HasValue)
			{
				var existingConfigItem = existingConfigItems[itemPlan.ExistingConfigItemId.Value];
				var desiredSecret = existingConfigItem.IsSecret || itemPlan.IsSecret;
				var shouldUpdateMetadata = !string.Equals(existingConfigItem.ValueType, itemPlan.ValueType, StringComparison.OrdinalIgnoreCase)
					|| existingConfigItem.IsSecret != desiredSecret;

				if (shouldUpdateMetadata)
				{
					existingConfigItem.ValueType = itemPlan.ValueType;
					existingConfigItem.IsSecret = desiredSecret;
					existingConfigItem.UpdatedAtUtc = now;
					updatedConfigItemCount += 1;
				}

				configItemIdByFullPath[itemPlan.FullPath] = existingConfigItem.Id;
				continue;
			}

			var createdConfigItem = new ConfigItemDefinition
			{
				Id = Guid.NewGuid(),
				ApplicationId = request.ApplicationId,
				NamespaceId = namespaceIdByPath[itemPlan.NamespacePath],
				Key = itemPlan.Key,
				FullPath = itemPlan.FullPath,
				ValueType = itemPlan.ValueType,
				IsSecret = itemPlan.IsSecret,
				IsRequired = false,
				DefaultRolloutPolicy = "immediate",
				ValidationSchemaJson = string.Empty,
				Description = "Imported from appsettings.",
				CreatedAtUtc = now,
				UpdatedAtUtc = now
			};

			dbContext.ConfigItems.Add(createdConfigItem);
			existingConfigItems[createdConfigItem.Id] = createdConfigItem;
			configItemIdByFullPath[itemPlan.FullPath] = createdConfigItem.Id;
			createdConfigItemCount += 1;
		}

		var allConfigItemIds = configItemIdByFullPath.Values.Distinct().ToList();
		var existingDraftValues = await dbContext.DraftValues
			.Where(x => allConfigItemIds.Contains(x.ConfigItemId) && x.ScopeType == scopeType && x.ScopeId == request.ScopeId)
			.ToDictionaryAsync(x => x.ConfigItemId, cancellationToken);

		var (actorUserId, actorUsername) = GetAuditActor(user);
		foreach (var itemPlan in plan.ConfigItems)
		{
			var configItemId = configItemIdByFullPath[itemPlan.FullPath];
			var isSecret = existingConfigItems[configItemId].IsSecret;
			var storedValueJson = ProtectDraftValueJson(itemPlan.ValueJson, isSecret, draftValueProtector);

			if (existingDraftValues.TryGetValue(configItemId, out var existingDraftValue))
			{
				existingDraftValue.ValueJson = storedValueJson;
				existingDraftValue.IsSecret = isSecret;
				existingDraftValue.ChangeNote = changeNote;
				existingDraftValue.UpdatedByUserId = actorUserId;
				existingDraftValue.UpdatedAtUtc = now;
				updatedDraftValueCount += 1;
				continue;
			}

			var draftValue = new DraftValue
			{
				Id = Guid.NewGuid(),
				ConfigItemId = configItemId,
				ScopeType = scopeType,
				ScopeId = request.ScopeId,
				ValueJson = storedValueJson,
				IsSecret = isSecret,
				ChangeNote = changeNote,
				UpdatedByUserId = actorUserId,
				UpdatedAtUtc = now
			};

			dbContext.DraftValues.Add(draftValue);
			createdDraftValueCount += 1;
		}

		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "config.import.appsettings.applied",
					targetType: "Application",
					targetIdentifier: request.ApplicationId.ToString(),
					targetDisplayName: request.ApplicationId.ToString(),
					outcome: "Succeeded",
					actorUserId: actorUserId,
					actorUsername: actorUsername,
					details: new Dictionary<string, object?>
					{
						["scopeType"] = scopeType.ToString(),
						["scopeId"] = request.ScopeId,
						["namespaceCount"] = plan.Namespaces.Count,
						["configItemCount"] = plan.ConfigItems.Count,
						["createdNamespaceCount"] = createdNamespaceCount,
						["createdConfigItemCount"] = createdConfigItemCount,
						["updatedConfigItemCount"] = updatedConfigItemCount,
						["createdDraftValueCount"] = createdDraftValueCount,
						["updatedDraftValueCount"] = updatedDraftValueCount,
						["conflictCount"] = CountImportConflicts(plan)
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: now)));

		await dbContext.SaveChangesAsync(cancellationToken);

		return Results.Ok(
			new AppSettingsImportApplyResponse(
				request.ApplicationId,
				scopeType.ToString(),
				request.ScopeId,
				plan.Namespaces.Count,
				plan.ConfigItems.Count,
				plan.ConfigItems.Count,
				createdNamespaceCount,
				createdConfigItemCount,
				updatedConfigItemCount,
				createdDraftValueCount,
				updatedDraftValueCount));
	})
	.RequirePermission(PermissionCatalog.ConfigWriteDraft, InstallationScopePath());

app.MapPost(
	"/api/v1/role-assignments",
	async (
		CreateRoleAssignmentRequest request,
		ClaimsPrincipal user,
		HttpContext httpContext,
		SecretManagerDbContext dbContext,
		IAuditEventWriter auditEventWriter,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request) ?? new Dictionary<string, string[]>();

		if (request.UserId == Guid.Empty)
		{
			validationErrors[nameof(request.UserId)] = ["UserId is required."];
		}

		if (request.RoleId == Guid.Empty)
		{
			validationErrors[nameof(request.RoleId)] = ["RoleId is required."];
		}

		if (request.ScopeId == Guid.Empty)
		{
			validationErrors[nameof(request.ScopeId)] = ["ScopeId is required."];
		}

		if (!Enum.TryParse<ResourceScopeType>(request.ScopeType.Trim(), ignoreCase: true, out var scopeType))
		{
			validationErrors[nameof(request.ScopeType)] = ["ScopeType is invalid."];
		}

		if (validationErrors.Count > 0)
		{
			return Results.ValidationProblem(validationErrors);
		}

		var targetUser = await dbContext.Users
			.AsNoTracking()
			.Where(x => x.Id == request.UserId)
			.Select(x => new
			{
				x.Id,
				x.Username
			})
			.FirstOrDefaultAsync(cancellationToken);

		if (targetUser is null)
		{
			return Results.Problem(
				title: "User not found",
				detail: $"User '{request.UserId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var role = await dbContext.RoleDefinitions
			.AsNoTracking()
			.FirstOrDefaultAsync(x => x.Id == request.RoleId, cancellationToken);

		if (role is null)
		{
			return Results.Problem(
				title: "Role not found",
				detail: $"Role '{request.RoleId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		var assignmentExists = await dbContext.RoleAssignments
			.AsNoTracking()
			.AnyAsync(
				x => x.UserId == request.UserId
					&& x.RoleDefinitionId == request.RoleId
					&& x.ScopeType == scopeType
					&& x.ScopeId == request.ScopeId,
				cancellationToken);

		if (assignmentExists)
		{
			return Results.Conflict();
		}

		var createdByUserId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
			? parsedUserId
			: (Guid?)null;

		var assignment = new RoleAssignment
		{
			Id = Guid.NewGuid(),
			UserId = request.UserId,
			RoleDefinitionId = request.RoleId,
			ScopeType = scopeType,
			ScopeId = request.ScopeId,
			CreatedByUserId = createdByUserId,
			CreatedAtUtc = DateTimeOffset.UtcNow
		};

		dbContext.RoleAssignments.Add(assignment);
		dbContext.AuditEvents.Add(
			auditEventWriter.Create(
				CreateAuditRequest(
					httpContext,
					action: "authorization.roleAssignment.created",
					targetType: "RoleAssignment",
					targetIdentifier: assignment.Id.ToString(),
					targetDisplayName: $"{role.Name} -> {targetUser.Username}",
					outcome: "Succeeded",
					actorUserId: createdByUserId,
					actorUsername: user.FindFirstValue(ClaimTypes.Name),
					details: new Dictionary<string, object?>
					{
						["assignedUserId"] = targetUser.Id,
						["assignedUsername"] = targetUser.Username,
						["roleId"] = role.Id,
						["roleName"] = role.Name,
						["scopeType"] = assignment.ScopeType.ToString(),
						["scopeId"] = assignment.ScopeId
					},
					installationId: Installation.SingletonId,
					occurredAtUtc: assignment.CreatedAtUtc)));
		await dbContext.SaveChangesAsync(cancellationToken);

		return Results.Created(
			$"/api/v1/role-assignments/{assignment.Id}",
			new CreateRoleAssignmentResponse(
				assignment.Id,
				assignment.UserId,
				assignment.RoleDefinitionId,
				assignment.ScopeType.ToString(),
				assignment.ScopeId,
				assignment.CreatedAtUtc,
				assignment.ExpiresAtUtc));
	})
	.RequirePermission(PermissionCatalog.RolesWrite, InstallationScopePath());

app.MapGet(
	"/api/v1/audit-events",
	async (
		int? take,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var normalizedTake = take.GetValueOrDefault(100);
		if (normalizedTake is < 1 or > 200)
		{
			return Results.ValidationProblem(new Dictionary<string, string[]>
			{
				[nameof(take)] = ["Take must be between 1 and 200."]
			});
		}

		var auditEvents = await dbContext.AuditEvents
			.AsNoTracking()
			.OrderByDescending(x => x.OccurredAtUtc)
			.Take(normalizedTake)
			.Select(x => new AuditEventSummaryResponse(
				x.Id,
				x.Action,
				x.Outcome,
				x.TargetType,
				x.TargetIdentifier,
				x.TargetDisplayName,
				x.ActorUserId,
				x.ActorUsername,
				x.OccurredAtUtc,
				x.CorrelationId))
			.ToListAsync(cancellationToken);

		return Results.Ok(auditEvents);
	})
	.RequirePermission(PermissionCatalog.AuditRead, InstallationScopePath());

app.MapGet(
	"/api/v1/audit-events/{eventId:guid}",
	async (
		Guid eventId,
		SecretManagerDbContext dbContext,
		CancellationToken cancellationToken) =>
	{
		var auditEvent = await dbContext.AuditEvents
			.AsNoTracking()
			.Where(x => x.Id == eventId)
			.Select(x => new AuditEventDetailResponse(
				x.Id,
				x.Action,
				x.Outcome,
				x.TargetType,
				x.TargetIdentifier,
				x.TargetDisplayName,
				x.ActorUserId,
				x.ActorUsername,
				x.OccurredAtUtc,
				x.CorrelationId,
				x.RemoteIpAddress,
				x.DetailsJson))
			.FirstOrDefaultAsync(cancellationToken);

		if (auditEvent is null)
		{
			return Results.Problem(
				title: "Audit event not found",
				detail: $"Audit event '{eventId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound);
		}

		return Results.Ok(auditEvent);
	})
	.RequirePermission(PermissionCatalog.AuditRead, InstallationScopePath());

app.MapGet(
	"/api/v1/bootstrap/status",
	async (IBootstrapService bootstrapService, CancellationToken cancellationToken) =>
	{
		var status = await bootstrapService.GetStatusAsync(cancellationToken);
		return Results.Ok(new BootstrapStatusResponse(status.IsInitialized, status.InstallationName));
	});

app.MapPost(
	"/api/v1/auth/bootstrap",
	async (
		BootstrapInstallationRequest request,
		IBootstrapService bootstrapService,
		IAuditEventWriter auditEventWriter,
		HttpContext httpContext,
		CancellationToken cancellationToken) =>
	{
		var validationErrors = Validate(request);
		if (validationErrors is not null)
		{
			return Results.ValidationProblem(validationErrors);
		}

		try
		{
			var result = await bootstrapService.BootstrapAsync(
				new BootstrapInstallationCommand(
					request.InstallationName,
					request.OwnerUsername,
					request.OwnerDisplayName,
					request.Password,
					httpContext.TraceIdentifier,
					httpContext.Connection.RemoteIpAddress?.ToString()),
				cancellationToken);

			return Results.Created(
				"/api/v1/auth/bootstrap",
				new BootstrapInstallationResponse(
					result.InstallationId,
					result.OwnerUserId,
					result.InstallationName,
					result.OwnerUsername));
		}
		catch (InvalidOperationException ex)
		{
			await auditEventWriter.WriteAsync(
				CreateAuditRequest(
					httpContext,
					action: "installation.bootstrap",
					targetType: "Installation",
					targetIdentifier: Installation.SingletonId.ToString(),
					targetDisplayName: request.InstallationName.Trim(),
					outcome: "Failed",
					actorUsername: request.OwnerUsername.Trim(),
					details: new Dictionary<string, object?>
					{
						["reason"] = "already_initialized"
					}),
				cancellationToken);

			return Results.Problem(
				detail: ex.Message,
				title: "Installation already initialized",
				statusCode: StatusCodes.Status409Conflict);
		}
	});

app.Run();

static Dictionary<string, string[]>? Validate<TRequest>(TRequest request) where TRequest : class
{
	var validationContext = new ValidationContext(request);
	var validationResults = new List<ValidationResult>();

	if (Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
	{
		return null;
	}

	return validationResults
		.SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty),
			(result, memberName) => new { memberName, result.ErrorMessage })
		.GroupBy(item => item.memberName)
		.ToDictionary(
			group => group.Key,
			group => group.Select(item => item.ErrorMessage ?? "Validation error.").ToArray());
}

static ResourceScope[] InstallationScopePath() =>
[
	new ResourceScope(ResourceScopeType.Installation, Installation.SingletonId)
];

static EnvironmentSummaryResponse ToEnvironmentResponse(EnvironmentDefinition environment)
{
	return new EnvironmentSummaryResponse(
		environment.Id,
		environment.Name,
		environment.Slug,
		environment.Description,
		environment.IsProtected,
		environment.CreatedAtUtc,
		environment.UpdatedAtUtc);
}

static ApplicationSummaryResponse ToApplicationResponse(ApplicationDefinition application)
{
	return new ApplicationSummaryResponse(
		application.Id,
		application.Name,
		application.Slug,
		application.Description,
		application.DefaultIntegrationMode,
		application.CreatedAtUtc,
		application.UpdatedAtUtc);
}

static NamespaceSummaryResponse ToNamespaceResponse(NamespaceDefinition catalogNamespace)
{
	return new NamespaceSummaryResponse(
		catalogNamespace.Id,
		catalogNamespace.ApplicationId,
		catalogNamespace.Name,
		catalogNamespace.Path,
		catalogNamespace.Description,
		catalogNamespace.CreatedAtUtc,
		catalogNamespace.UpdatedAtUtc);
}

static NodeGroupSummaryResponse ToNodeGroupResponse(NodeGroupDefinition nodeGroup)
{
	return new NodeGroupSummaryResponse(
		nodeGroup.Id,
		nodeGroup.EnvironmentId,
		nodeGroup.Name,
		nodeGroup.Slug,
		nodeGroup.Description,
		nodeGroup.CreatedAtUtc,
		nodeGroup.UpdatedAtUtc);
}

static ManagedNodeSummaryResponse ToManagedNodeResponse(ManagedNodeRecord node)
{
	return new ManagedNodeSummaryResponse(
		node.Id,
		node.EnvironmentId,
		node.NodeGroupId,
		node.Name,
		node.Hostname,
		node.Platform,
		node.Status,
		node.LastSeenAtUtc,
		node.AgentVersion,
		node.RolloutPolicyDefault,
		node.CreatedAtUtc,
		node.UpdatedAtUtc);
}

static ApplicationAssignmentResponse ToApplicationAssignmentResponse(ApplicationAssignment assignment)
{
	return new ApplicationAssignmentResponse(
		assignment.Id,
		assignment.ApplicationId,
		assignment.EnvironmentId,
		assignment.NodeGroupId,
		assignment.ManagedNodeId,
		assignment.Enabled,
		assignment.CreatedAtUtc);
}

static ConfigItemSummaryResponse ToConfigItemResponse(ConfigItemDefinition configItem)
{
	return new ConfigItemSummaryResponse(
		configItem.Id,
		configItem.ApplicationId,
		configItem.NamespaceId,
		configItem.Key,
		configItem.FullPath,
		configItem.ValueType,
		configItem.IsSecret,
		configItem.IsRequired,
		configItem.DefaultRolloutPolicy,
		configItem.ValidationSchemaJson,
		configItem.Description,
		configItem.CreatedAtUtc,
		configItem.UpdatedAtUtc);
}

static DraftValueResponse ToDraftValueResponse(DraftValue draftValue)
{
	return new DraftValueResponse(
		draftValue.Id,
		draftValue.ConfigItemId,
		draftValue.ScopeType.ToString(),
		draftValue.ScopeId,
		draftValue.IsSecret ? null : draftValue.ValueJson,
		draftValue.IsSecret,
		draftValue.IsSecret,
		draftValue.ChangeNote,
		draftValue.UpdatedByUserId,
		draftValue.UpdatedAtUtc);
}

static EffectivePreviewResponse ToEffectivePreviewResponse(
	Guid applicationId,
	Guid environmentId,
	Guid managedNodeId,
	Guid? nodeGroupId,
	IReadOnlyList<EffectivePreviewItem> resolvedItems,
	bool canRevealSecrets,
	IDraftValueProtector draftValueProtector)
{
	var items = resolvedItems
		.Select(x => new EffectivePreviewItemResponse(
			x.DraftValueId,
			x.ConfigItemId,
			x.FullPath,
			x.ValueType,
			x.IsSecret && !canRevealSecrets
				? null
				: x.IsSecret
					? draftValueProtector.Unprotect(x.ValueJson)
					: x.ValueJson,
			x.IsSecret,
			x.IsSecret && !canRevealSecrets,
			x.SourceScopeType.ToString(),
			x.SourceScopeId,
			x.UpdatedAtUtc))
		.ToList();

	return new EffectivePreviewResponse(
		applicationId,
		environmentId,
		managedNodeId,
		nodeGroupId,
		items.Count,
		items);
}

static PublishOperationResponse ToPublishOperationResponse(PublishOperation publishOperation)
{
	return new PublishOperationResponse(
		publishOperation.Id,
		publishOperation.EnvironmentId,
		publishOperation.ApplicationId,
		publishOperation.InitiatedByUserId,
		publishOperation.ChangeSummary,
		publishOperation.Status,
		publishOperation.CreatedAtUtc,
		publishOperation.CompletedAtUtc);
}

static PublishedVersionResponse ToPublishedVersionResponse(PublishedVersion publishedVersion)
{
	return new PublishedVersionResponse(
		publishedVersion.Id,
		publishedVersion.PublishOperationId,
		publishedVersion.EnvironmentId,
		publishedVersion.ApplicationId,
		publishedVersion.VersionNumber,
		publishedVersion.RolloutPolicy,
		publishedVersion.ContentHash,
		publishedVersion.PublishedByUserId,
		publishedVersion.PublishedAtUtc,
		publishedVersion.SupersedesVersionId);
}

static AgentStatusResponse ToAgentStatusResponse(AgentRegistration agentRegistration, ManagedNodeRecord managedNode)
{
	return new AgentStatusResponse(
		agentRegistration.Id,
		managedNode.Id,
		managedNode.EnvironmentId,
		managedNode.NodeGroupId,
		managedNode.Hostname,
		managedNode.AgentVersion,
		agentRegistration.LastSeenAtUtc,
		ResolveAgentHealthStatus(agentRegistration.LastSeenAtUtc, agentRegistration.HealthStatus),
		agentRegistration.CurrentPublishedVersionId,
		agentRegistration.CurrentVersionNumber);
}

static AgentSnapshotResponse BuildCanonicalAgentSnapshotResponse(
	ManagedNodeRecord managedNode,
	PublishedVersion publishedVersion,
	IDraftValueProtector draftValueProtector)
{
	var payload = DeserializePublishedVersionPayload(publishedVersion.PayloadJson);
	var resolvedValues = EffectivePreviewResolver.Resolve(
		new EffectivePreviewTarget(publishedVersion.ApplicationId, publishedVersion.EnvironmentId, managedNode.NodeGroupId, managedNode.Id),
		payload.DraftValues.Select(x => new EffectivePreviewCandidate(
			x.DraftValueId,
			x.ConfigItemId,
			x.FullPath,
			x.ValueType,
			x.ValueJson,
			x.IsSecret,
			Enum.Parse<ResourceScopeType>(x.ScopeType, ignoreCase: true),
			x.ScopeId,
			x.UpdatedAtUtc)));

	var values = resolvedValues
		.Select(x => new AgentSnapshotValueResponse(
			x.ConfigItemId,
			x.FullPath,
			x.ValueType,
			x.IsSecret ? draftValueProtector.Unprotect(x.ValueJson) : x.ValueJson,
			x.IsSecret,
			x.SourceScopeType.ToString(),
			x.SourceScopeId))
		.ToList();

	var snapshotId = BuildSnapshotId(publishedVersion.Id, managedNode.Id, publishedVersion.ApplicationId);
	var snapshotHash = ComputeAgentSnapshotHash(
		snapshotId,
		managedNode.Id,
		publishedVersion.ApplicationId,
		publishedVersion.Id,
		publishedVersion.VersionNumber,
		publishedVersion.RolloutPolicy,
		publishedVersion.PublishedAtUtc,
		values);

	return new AgentSnapshotResponse(
		snapshotId,
		managedNode.Id,
		publishedVersion.ApplicationId,
		publishedVersion.Id,
		publishedVersion.VersionNumber,
		snapshotHash,
		publishedVersion.RolloutPolicy,
		publishedVersion.PublishedAtUtc,
		"canonical",
		values);
}

static async Task<AgentEnrollmentToken?> FindMatchingEnrollmentTokenAsync(
	Guid managedNodeId,
	string enrollmentToken,
	SecretManagerDbContext dbContext,
	Argon2PasswordHasher passwordHasher,
	CancellationToken cancellationToken)
{
	var enrollmentTokens = await dbContext.AgentEnrollmentTokens
		.Where(x => x.ManagedNodeId == managedNodeId)
		.ToListAsync(cancellationToken);

	return enrollmentTokens.FirstOrDefault(x => passwordHasher.Verify(enrollmentToken, x.TokenHash));
}

static async Task<(AgentRegistration? AgentRegistration, ManagedNodeRecord? ManagedNode)> AuthenticateAgentAsync(
	Guid agentId,
	string agentCredential,
	SecretManagerDbContext dbContext,
	Argon2PasswordHasher passwordHasher,
	CancellationToken cancellationToken)
{
	var agentRegistration = await dbContext.AgentRegistrations
		.FirstOrDefaultAsync(x => x.Id == agentId, cancellationToken);
	if (agentRegistration is null || !passwordHasher.Verify(agentCredential, agentRegistration.CredentialHash))
	{
		return (null, null);
	}

	var managedNode = await dbContext.ManagedNodes.FirstOrDefaultAsync(x => x.Id == agentRegistration.ManagedNodeId, cancellationToken);
	return (agentRegistration, managedNode);
}

static async Task<IReadOnlyList<AgentEnrollmentAssignmentResponse>> BuildInitialAgentAssignmentsAsync(
	ManagedNodeRecord managedNode,
	SecretManagerDbContext dbContext,
	CancellationToken cancellationToken)
{
	var assignedApplicationIds = await dbContext.ApplicationAssignments
		.AsNoTracking()
		.Where(x => x.EnvironmentId == managedNode.EnvironmentId && x.Enabled)
		.Where(x => x.ManagedNodeId == managedNode.Id || (managedNode.NodeGroupId.HasValue && x.NodeGroupId == managedNode.NodeGroupId.Value))
		.Select(x => x.ApplicationId)
		.Distinct()
		.ToListAsync(cancellationToken);

		if (assignedApplicationIds.Count == 0)
		{
			return [];
		}

	var publishedVersions = await dbContext.PublishedVersions
		.AsNoTracking()
		.Where(x => x.EnvironmentId == managedNode.EnvironmentId && assignedApplicationIds.Contains(x.ApplicationId))
		.Select(x => new { x.ApplicationId, x.Id, x.VersionNumber })
		.ToListAsync(cancellationToken);

	var latestPublishedVersionByApplicationId = publishedVersions
		.GroupBy(x => x.ApplicationId)
		.ToDictionary(
			group => group.Key,
			group => group.OrderByDescending(x => x.VersionNumber).First());

	return assignedApplicationIds
		.OrderBy(x => x)
		.Select(applicationId => latestPublishedVersionByApplicationId.TryGetValue(applicationId, out var publishedVersion)
			? new AgentEnrollmentAssignmentResponse(applicationId, publishedVersion.Id, publishedVersion.VersionNumber)
			: new AgentEnrollmentAssignmentResponse(applicationId, null, null))
		.ToList();
}

static async Task<IReadOnlyList<Guid>> ResolveTargetAgentIdsAsync(
	Guid environmentId,
	Guid applicationId,
	SecretManagerDbContext dbContext,
	CancellationToken cancellationToken)
{
	var assignments = await dbContext.ApplicationAssignments
		.AsNoTracking()
		.Where(x => x.EnvironmentId == environmentId && x.ApplicationId == applicationId && x.Enabled)
		.Select(x => new { x.ManagedNodeId, x.NodeGroupId })
		.ToListAsync(cancellationToken);
	if (assignments.Count == 0)
	{
		return [];
	}

	var agents = await (
		from agent in dbContext.AgentRegistrations.AsNoTracking()
		join node in dbContext.ManagedNodes.AsNoTracking() on agent.ManagedNodeId equals node.Id
		where node.EnvironmentId == environmentId
		select new
		{
			AgentId = agent.Id,
			ManagedNodeId = node.Id,
			node.NodeGroupId
		})
		.ToListAsync(cancellationToken);

	return agents
		.Where(agent => assignments.Any(assignment =>
			assignment.ManagedNodeId == agent.ManagedNodeId
			|| (assignment.NodeGroupId.HasValue && agent.NodeGroupId == assignment.NodeGroupId)))
		.Select(agent => agent.AgentId)
		.Distinct()
		.ToList();
}

static string ResolveAgentHealthStatus(DateTimeOffset? lastSeenAtUtc, string storedHealthStatus)
{
	if (!lastSeenAtUtc.HasValue)
	{
		return storedHealthStatus.Length == 0 ? "Pending" : storedHealthStatus;
	}

	return DateTimeOffset.UtcNow - lastSeenAtUtc.Value > TimeSpan.FromMinutes(5)
		? "Degraded"
		: "Online";
}

static async Task<(PublishOperation PublishOperation, PublishedVersion PublishedVersion)> CreateImmutablePublishedVersionAsync(
	Guid environmentId,
	Guid applicationId,
	string changeSummary,
	string rolloutPolicy,
	string payloadJson,
	Guid? actorUserId,
	SecretManagerDbContext dbContext,
	CancellationToken cancellationToken)
{
	var latestVersion = await dbContext.PublishedVersions
		.AsNoTracking()
		.Where(x => x.EnvironmentId == environmentId && x.ApplicationId == applicationId)
		.OrderByDescending(x => x.VersionNumber)
		.Select(x => new { x.Id, x.VersionNumber })
		.FirstOrDefaultAsync(cancellationToken);

	var now = DateTimeOffset.UtcNow;
	var publishOperation = new PublishOperation
	{
		Id = Guid.NewGuid(),
		EnvironmentId = environmentId,
		ApplicationId = applicationId,
		InitiatedByUserId = actorUserId,
		ChangeSummary = changeSummary,
		Status = "Completed",
		CreatedAtUtc = now,
		CompletedAtUtc = now
	};

	var publishedVersion = new PublishedVersion
	{
		Id = Guid.NewGuid(),
		PublishOperationId = publishOperation.Id,
		EnvironmentId = environmentId,
		ApplicationId = applicationId,
		VersionNumber = latestVersion?.VersionNumber + 1 ?? 1,
		RolloutPolicy = rolloutPolicy,
		PayloadJson = payloadJson,
		ContentHash = ComputeSha256(payloadJson),
		PublishedByUserId = actorUserId,
		PublishedAtUtc = now,
		SupersedesVersionId = latestVersion?.Id
	};

	return (publishOperation, publishedVersion);
}

static string BuildSnapshotId(Guid publishedVersionId, Guid managedNodeId, Guid applicationId)
{
	return $"{publishedVersionId:N}.{managedNodeId:N}.{applicationId:N}";
}

static bool TryParseSnapshotId(string snapshotId, out Guid publishedVersionId, out Guid managedNodeId, out Guid applicationId)
{
	publishedVersionId = Guid.Empty;
	managedNodeId = Guid.Empty;
	applicationId = Guid.Empty;

	var parts = snapshotId.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	return parts.Length == 3
		&& Guid.TryParseExact(parts[0], "N", out publishedVersionId)
		&& Guid.TryParseExact(parts[1], "N", out managedNodeId)
		&& Guid.TryParseExact(parts[2], "N", out applicationId);
}

static string ComputeAgentSnapshotHash(
	string snapshotId,
	Guid managedNodeId,
	Guid applicationId,
	Guid publishedVersionId,
	int versionNumber,
	string rolloutPolicy,
	DateTimeOffset updatedAtUtc,
	IReadOnlyList<AgentSnapshotValueResponse> values)
{
	return AgentSnapshotIntegrity.ComputeHash(
		snapshotId,
		managedNodeId,
		applicationId,
		publishedVersionId,
		versionNumber,
		rolloutPolicy,
		updatedAtUtc,
		values);
}

static bool ValidateAgentSnapshotHash(AgentSnapshotResponse snapshot)
{
	return AgentSnapshotIntegrity.Validate(snapshot);
}

static AuditEventWriteRequest CreateAuditRequest(
	HttpContext httpContext,
	string action,
	string targetType,
	string targetIdentifier,
	string? targetDisplayName,
	string outcome,
	Guid? actorUserId = null,
	string? actorUsername = null,
	IReadOnlyDictionary<string, object?>? details = null,
	Guid? installationId = null,
	DateTimeOffset? occurredAtUtc = null)
{
	return new AuditEventWriteRequest(
		Action: action,
		TargetType: targetType,
		TargetIdentifier: targetIdentifier,
		TargetDisplayName: targetDisplayName,
		Outcome: outcome,
		CorrelationId: httpContext.TraceIdentifier,
		ActorUserId: actorUserId,
		ActorUsername: actorUsername,
		RemoteIpAddress: httpContext.Connection.RemoteIpAddress?.ToString(),
		Details: details,
		InstallationId: installationId,
		OccurredAtUtc: occurredAtUtc);
}

static (Guid? UserId, string? Username) GetAuditActor(ClaimsPrincipal user)
{
	var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
		? parsedUserId
		: (Guid?)null;

	return (userId, user.FindFirstValue(ClaimTypes.Name));
}

static async Task<bool> UserHasPermissionAsync(
	ClaimsPrincipal user,
	string permission,
	IPermissionEvaluator permissionEvaluator,
	CancellationToken cancellationToken)
{
	var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
	if (!Guid.TryParse(userId, out var parsedUserId))
	{
		return false;
	}

	var evaluation = await permissionEvaluator.EvaluateAsync(
		new PermissionEvaluationRequest(parsedUserId, permission, InstallationScopePath()),
		cancellationToken);

	return evaluation.IsAllowed;
}

static string NormalizeRequiredText(string value)
{
	return string.IsNullOrWhiteSpace(value)
		? string.Empty
		: value.Trim();
}

static string NormalizeOptionalText(string? value)
{
	return string.IsNullOrWhiteSpace(value)
		? string.Empty
		: value.Trim();
}

static string NormalizeOptionalTextOrDefault(string? value, string fallback)
{
	var normalizedValue = NormalizeOptionalText(value);
	return normalizedValue.Length == 0
		? fallback
		: normalizedValue;
}

static string NormalizeCatalogKeyword(string? value, string fallback)
{
	return NormalizeOptionalTextOrDefault(value, fallback).ToLowerInvariant();
}

static PublishedVersionPayloadDocument DeserializePublishedVersionPayload(string payloadJson)
{
	return JsonSerializer.Deserialize<PublishedVersionPayloadDocument>(payloadJson)
		?? throw new InvalidOperationException("Published version payload is invalid.");
}

static int GetDraftScopeOrderFromName(string scopeType)
{
	return Enum.TryParse<ResourceScopeType>(scopeType, ignoreCase: true, out var parsedScopeType)
		? GetDraftScopeOrder(parsedScopeType)
		: int.MaxValue;
}

static int GetDraftScopeOrder(ResourceScopeType scopeType)
{
	return scopeType switch
	{
		ResourceScopeType.Application => 0,
		ResourceScopeType.Environment => 1,
		ResourceScopeType.NodeGroup => 2,
		ResourceScopeType.ManagedNode => 3,
		ResourceScopeType.EmergencyOverride => 4,
		_ => int.MaxValue
	};
}

static string NormalizeHostname(string value)
{
	var normalizedValue = NormalizeRequiredText(value);
	return normalizedValue.Length == 0
		? string.Empty
		: normalizedValue.ToLowerInvariant();
}

static string NormalizeSlug(string? slug, string fallbackName)
{
	var source = string.IsNullOrWhiteSpace(slug)
		? fallbackName
		: slug.Trim();

	if (source.Length == 0)
	{
		return string.Empty;
	}

	var builder = new StringBuilder(source.Length);
	var previousWasSeparator = false;

	foreach (var character in source.ToLowerInvariant())
	{
		var normalizedCharacter = character is >= 'a' and <= 'z' or >= '0' and <= '9'
			? character
			: '-';

		if (normalizedCharacter == '-')
		{
			if (previousWasSeparator || builder.Length == 0)
			{
				continue;
			}

			previousWasSeparator = true;
			builder.Append('-');
			continue;
		}

		previousWasSeparator = false;
		builder.Append(normalizedCharacter);
	}

	if (builder.Length > 0 && builder[^1] == '-')
	{
		builder.Length -= 1;
	}

	return builder.ToString();
}

static string NormalizeNamespacePath(string? value, string fallbackName)
{
	var source = NormalizeOptionalTextOrDefault(value, fallbackName);
	if (source.Length == 0)
	{
		return string.Empty;
	}

	var segments = source.Split([':', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	return string.Join(":", segments);
}

static string NormalizeConfigItemKey(string value)
{
	return NormalizeRequiredText(value).Trim(':', '/', '\\');
}

static bool ContainsPathSeparator(string value)
{
	return value.IndexOfAny([':', '/', '\\']) >= 0;
}

static string BuildConfigItemFullPath(string namespacePath, string key)
{
	return namespacePath.Length == 0
		? key
		: $"{namespacePath}:{key}";
}

static bool TryNormalizeJsonDocument(string? value, out string normalizedJson, out string? error)
{
	normalizedJson = NormalizeOptionalText(value);
	if (normalizedJson.Length == 0)
	{
		error = null;
		return true;
	}

	try
	{
		using var document = JsonDocument.Parse(normalizedJson);
		normalizedJson = document.RootElement.GetRawText();
		error = null;
		return true;
	}
	catch (JsonException ex)
	{
		error = ex.Message;
		return false;
	}
}

static async Task<IResult?> ValidateImportScopeAsync(
	ResourceScopeType scopeType,
	Guid scopeId,
	SecretManagerDbContext dbContext,
	CancellationToken cancellationToken)
{
	return scopeType switch
	{
		ResourceScopeType.Installation when scopeId != Installation.SingletonId => Results.Problem(
			title: "Scope not found",
			detail: $"Installation scope '{scopeId}' does not exist.",
			statusCode: StatusCodes.Status404NotFound),
		ResourceScopeType.Installation => null,
		ResourceScopeType.Environment => await dbContext.Environments.AsNoTracking().AnyAsync(x => x.Id == scopeId, cancellationToken)
			? null
			: Results.Problem(
				title: "Scope not found",
				detail: $"Environment scope '{scopeId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound),
		ResourceScopeType.NodeGroup => await dbContext.NodeGroups.AsNoTracking().AnyAsync(x => x.Id == scopeId, cancellationToken)
			? null
			: Results.Problem(
				title: "Scope not found",
				detail: $"Node group scope '{scopeId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound),
		ResourceScopeType.ManagedNode => await dbContext.ManagedNodes.AsNoTracking().AnyAsync(x => x.Id == scopeId, cancellationToken)
			? null
			: Results.Problem(
				title: "Scope not found",
				detail: $"Managed node scope '{scopeId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound),
		_ => Results.ValidationProblem(new Dictionary<string, string[]>
		{
			[nameof(AppSettingsImportPreviewRequest.ScopeType)] = ["ScopeType must be Installation, Environment, NodeGroup, or ManagedNode."]
		})
	};
}

static bool TryParseImportScopeType(string? value, out ResourceScopeType scopeType)
{
	if (!Enum.TryParse<ResourceScopeType>(NormalizeOptionalText(value), ignoreCase: true, out scopeType))
	{
		return false;
	}

	return scopeType is ResourceScopeType.Installation
		or ResourceScopeType.Environment
		or ResourceScopeType.NodeGroup
		or ResourceScopeType.ManagedNode;
}

static bool TryParseDraftScopeType(string? value, out ResourceScopeType scopeType)
{
	if (!Enum.TryParse<ResourceScopeType>(NormalizeOptionalText(value), ignoreCase: true, out scopeType))
	{
		return false;
	}

	return scopeType is ResourceScopeType.Application
		or ResourceScopeType.Environment
		or ResourceScopeType.NodeGroup
		or ResourceScopeType.ManagedNode
		or ResourceScopeType.EmergencyOverride;
}

static async Task<IResult?> ValidateDraftScopeAsync(
	ResourceScopeType scopeType,
	Guid scopeId,
	Guid applicationId,
	SecretManagerDbContext dbContext,
	CancellationToken cancellationToken)
{
	return scopeType switch
	{
		ResourceScopeType.Application when scopeId != applicationId => Results.ValidationProblem(new Dictionary<string, string[]>
		{
			[nameof(CreateDraftValueRequest.ScopeId)] = ["Application scope must match the owning application id."]
		}),
		ResourceScopeType.Application => null,
		ResourceScopeType.Environment => await dbContext.Environments.AsNoTracking().AnyAsync(x => x.Id == scopeId, cancellationToken)
			? null
			: Results.Problem(
				title: "Scope not found",
				detail: $"Environment scope '{scopeId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound),
		ResourceScopeType.NodeGroup => await dbContext.NodeGroups.AsNoTracking().AnyAsync(x => x.Id == scopeId, cancellationToken)
			? null
			: Results.Problem(
				title: "Scope not found",
				detail: $"Node group scope '{scopeId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound),
		ResourceScopeType.ManagedNode => await dbContext.ManagedNodes.AsNoTracking().AnyAsync(x => x.Id == scopeId, cancellationToken)
			? null
			: Results.Problem(
				title: "Scope not found",
				detail: $"Managed node scope '{scopeId}' does not exist.",
				statusCode: StatusCodes.Status404NotFound),
		ResourceScopeType.EmergencyOverride when scopeId != Installation.SingletonId => Results.ValidationProblem(new Dictionary<string, string[]>
		{
			[nameof(CreateDraftValueRequest.ScopeId)] = [$"EmergencyOverride scope must use '{Installation.SingletonId}'."]
		}),
		ResourceScopeType.EmergencyOverride => null,
		_ => Results.ValidationProblem(new Dictionary<string, string[]>
		{
			[nameof(CreateDraftValueRequest.ScopeType)] = ["ScopeType must be Application, Environment, NodeGroup, ManagedNode, or EmergencyOverride."]
		})
	};
}

static string ProtectDraftValueJson(string normalizedValueJson, bool isSecret, IDraftValueProtector draftValueProtector)
{
	return isSecret
		? draftValueProtector.Protect(normalizedValueJson)
		: normalizedValueJson;
}

static string BuildPublishedVersionItemKey(PublishedVersionPayloadItem item)
{
	return $"{item.ConfigItemId:N}:{item.ScopeType}:{item.ScopeId:N}";
}

static bool PublishedVersionItemsEqual(PublishedVersionPayloadItem left, PublishedVersionPayloadItem right)
{
	return left.ConfigItemId == right.ConfigItemId
		&& string.Equals(left.FullPath, right.FullPath, StringComparison.Ordinal)
		&& string.Equals(left.ValueType, right.ValueType, StringComparison.Ordinal)
		&& left.IsSecret == right.IsSecret
		&& string.Equals(left.ScopeType, right.ScopeType, StringComparison.Ordinal)
		&& left.ScopeId == right.ScopeId
		&& string.Equals(left.ValueJson, right.ValueJson, StringComparison.Ordinal)
		&& string.Equals(left.ChangeNote, right.ChangeNote, StringComparison.Ordinal);
}

static string? FormatPublishedVersionValueJson(
	string? valueJson,
	bool isSecret,
	bool canRevealSecrets,
	IDraftValueProtector draftValueProtector)
{
	if (valueJson is null)
	{
		return null;
	}

	if (!isSecret)
	{
		return valueJson;
	}

	return canRevealSecrets
		? draftValueProtector.Unprotect(valueJson)
		: null;
}

static bool IsPublishScopeRelevant(
	ResourceScopeType scopeType,
	Guid scopeId,
	Guid applicationId,
	Guid environmentId,
	IReadOnlySet<Guid> environmentNodeGroupIds,
	IReadOnlySet<Guid> environmentManagedNodeIds)
{
	return scopeType switch
	{
		ResourceScopeType.Installation => scopeId == Installation.SingletonId,
		ResourceScopeType.Application => scopeId == applicationId,
		ResourceScopeType.Environment => scopeId == environmentId,
		ResourceScopeType.NodeGroup => environmentNodeGroupIds.Contains(scopeId),
		ResourceScopeType.ManagedNode => environmentManagedNodeIds.Contains(scopeId),
		ResourceScopeType.EmergencyOverride => scopeId == Installation.SingletonId,
		_ => false
	};
}

static string BuildPublishedVersionPayloadJson(
	Guid environmentId,
	Guid applicationId,
	string rolloutPolicy,
	IReadOnlyList<PublishedVersionPayloadItem> draftValues)
{
	return JsonSerializer.Serialize(new PublishedVersionPayloadDocument(
		environmentId,
		applicationId,
		rolloutPolicy,
		draftValues));
}

static string ComputeSha256(string value)
{
	var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
	return Convert.ToHexString(hashBytes).ToLowerInvariant();
}

static string GenerateOpaqueSecret()
{
	return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}

static async Task<(AppSettingsImportPlan? Plan, Dictionary<string, string[]>? ValidationErrors)> BuildAppSettingsImportPlanAsync(
	Guid applicationId,
	string jsonPayload,
	IReadOnlyCollection<string>? secretFullPaths,
	ResourceScopeType scopeType,
	Guid scopeId,
	SecretManagerDbContext dbContext,
	CancellationToken cancellationToken)
{
	if (!TryParseAppSettingsPayload(jsonPayload, secretFullPaths, out var parsedItems, out var error))
	{
		return (null, new Dictionary<string, string[]>
		{
			[nameof(AppSettingsImportPreviewRequest.JsonPayload)] = [$"JsonPayload is invalid. {error}"]
		});
	}

	var existingNamespaces = await dbContext.Namespaces
		.AsNoTracking()
		.Where(x => x.ApplicationId == applicationId)
		.Select(x => new { x.Id, x.Path })
		.ToListAsync(cancellationToken);

	var namespaceIdByPath = existingNamespaces.ToDictionary(x => x.Path, x => x.Id, StringComparer.OrdinalIgnoreCase);

	var existingConfigItems = await dbContext.ConfigItems
		.AsNoTracking()
		.Where(x => x.ApplicationId == applicationId)
		.Select(x => new { x.Id, x.FullPath, x.IsSecret, x.ValueType })
		.ToListAsync(cancellationToken);

	var configItemsByFullPath = existingConfigItems.ToDictionary(x => x.FullPath, StringComparer.OrdinalIgnoreCase);
	var matchedConfigItemIds = parsedItems
		.Where(x => configItemsByFullPath.ContainsKey(x.FullPath))
		.Select(x => configItemsByFullPath[x.FullPath].Id)
		.Distinct()
		.ToList();

	var existingDraftValueIdsByConfigItemId = matchedConfigItemIds.Count == 0
		? new Dictionary<Guid, Guid>()
		: await dbContext.DraftValues
			.AsNoTracking()
			.Where(x => matchedConfigItemIds.Contains(x.ConfigItemId) && x.ScopeType == scopeType && x.ScopeId == scopeId)
			.Select(x => new { x.Id, x.ConfigItemId })
			.ToDictionaryAsync(x => x.ConfigItemId, x => x.Id, cancellationToken);

	var namespacePlans = parsedItems
		.Select(x => x.NamespacePath)
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
		.Select(path => new AppSettingsImportNamespacePlan(
			BuildImportedNamespaceName(path),
			path,
			namespaceIdByPath.TryGetValue(path, out var namespaceId) ? namespaceId : null))
		.ToList();

	var configItemPlans = parsedItems
		.Select(item =>
		{
			configItemsByFullPath.TryGetValue(item.FullPath, out var existingConfigItem);
			return new AppSettingsImportConfigItemPlan(
				item.NamespacePath,
				item.Key,
				item.FullPath,
				item.ValueType,
				item.ValueJson,
				item.IsSecret,
				namespaceIdByPath.TryGetValue(item.NamespacePath, out var namespaceId) ? namespaceId : null,
				existingConfigItem?.Id,
				existingConfigItem is null
					? null
					: existingDraftValueIdsByConfigItemId.TryGetValue(existingConfigItem.Id, out var draftValueId)
						? draftValueId
						: null,
				existingConfigItem?.IsSecret ?? false,
				existingConfigItem?.ValueType ?? string.Empty);
		})
		.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
		.ToList();

	return (new AppSettingsImportPlan(applicationId, scopeType, scopeId, namespacePlans, configItemPlans), null);
}

static AppSettingsImportPreviewResponse ToAppSettingsImportPreviewResponse(AppSettingsImportPlan plan)
{
	return new AppSettingsImportPreviewResponse(
		plan.ApplicationId,
		plan.ScopeType.ToString(),
		plan.ScopeId,
		plan.Namespaces.Select(x => new AppSettingsImportNamespacePreviewResponse(
			x.Name,
			x.Path,
			x.ExistingNamespaceId.HasValue)).ToList(),
		plan.ConfigItems.Select(x => new AppSettingsImportConfigItemPreviewResponse(
			x.NamespacePath,
			x.Key,
			x.FullPath,
			x.ValueType,
			x.IsSecret,
			x.ExistingNamespaceId.HasValue,
			x.ExistingConfigItemId,
			x.ExistingDraftValueId.HasValue)).ToList(),
		CountImportConflicts(plan));
}

static int CountImportConflicts(AppSettingsImportPlan plan)
{
	return plan.ConfigItems.Count(x => x.ExistingConfigItemId.HasValue || x.ExistingDraftValueId.HasValue);
}

static bool TryParseAppSettingsPayload(
	string jsonPayload,
	IReadOnlyCollection<string>? secretFullPaths,
	out IReadOnlyList<ParsedAppSettingsConfigItem> parsedItems,
	out string? error)
{
	parsedItems = [];
	error = null;
	var normalizedPayload = NormalizeOptionalText(jsonPayload);
	if (normalizedPayload.Length == 0)
	{
		error = "JsonPayload is required.";
		return false;
	}

	try
	{
		using var document = JsonDocument.Parse(normalizedPayload);
		if (document.RootElement.ValueKind != JsonValueKind.Object)
		{
			error = "Root JSON value must be an object.";
			return false;
		}

		var normalizedSecretFullPaths = new HashSet<string>(
			(secretFullPaths ?? [])
				.Select(NormalizeImportedFullPath)
				.Where(x => x.Length > 0),
			StringComparer.OrdinalIgnoreCase);

		var itemsByFullPath = new Dictionary<string, ParsedAppSettingsConfigItem>(StringComparer.OrdinalIgnoreCase);
		VisitAppSettingsObject(document.RootElement, [], itemsByFullPath, normalizedSecretFullPaths, out error);
		if (error is not null)
		{
			return false;
		}

		if (itemsByFullPath.Count == 0)
		{
			error = "JsonPayload must contain at least one scalar, null, or array configuration value.";
			return false;
		}

		parsedItems = itemsByFullPath.Values.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
		return true;
	}
	catch (JsonException ex)
	{
		error = ex.Message;
		return false;
	}
}

static void VisitAppSettingsObject(
	JsonElement element,
	IReadOnlyList<string> parentSegments,
	Dictionary<string, ParsedAppSettingsConfigItem> itemsByFullPath,
	ISet<string> secretFullPaths,
	out string? error)
{
	error = null;

	foreach (var property in element.EnumerateObject())
	{
		var propertySegments = SplitImportPathSegments(property.Name);
		if (propertySegments.Length == 0)
		{
			continue;
		}

		var currentSegments = new List<string>(parentSegments.Count + propertySegments.Length);
		currentSegments.AddRange(parentSegments);
		currentSegments.AddRange(propertySegments);

		if (property.Value.ValueKind == JsonValueKind.Object)
		{
			VisitAppSettingsObject(property.Value, currentSegments, itemsByFullPath, secretFullPaths, out error);
			if (error is not null)
			{
				return;
			}

			continue;
		}

		var key = currentSegments[^1];
		var namespacePath = currentSegments.Count == 1
			? string.Empty
			: string.Join(":", currentSegments.Take(currentSegments.Count - 1));
		var fullPath = BuildConfigItemFullPath(namespacePath, key);

		if (itemsByFullPath.ContainsKey(fullPath))
		{
			error = $"Duplicate configuration path '{fullPath}' detected in JsonPayload.";
			return;
		}

		itemsByFullPath[fullPath] = new ParsedAppSettingsConfigItem(
			namespacePath,
			key,
			fullPath,
			InferImportedValueType(property.Value),
			property.Value.GetRawText(),
			secretFullPaths.Contains(fullPath));
	}
}

static string[] SplitImportPathSegments(string value)
{
	return NormalizeRequiredText(value)
		.Split([':', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static string NormalizeImportedFullPath(string value)
{
	var segments = SplitImportPathSegments(value);
	return segments.Length == 0
		? string.Empty
		: string.Join(":", segments);
}

static string BuildImportedNamespaceName(string namespacePath)
{
	if (namespacePath.Length == 0)
	{
		return "Root";
	}

	var segments = SplitImportPathSegments(namespacePath);
	return segments.Length == 0
		? "Root"
		: segments[^1];
}

static string InferImportedValueType(JsonElement value)
{
	return value.ValueKind switch
	{
		JsonValueKind.True or JsonValueKind.False => "boolean",
		JsonValueKind.Number when value.TryGetInt64(out _) => "integer",
		JsonValueKind.Number => "number",
		JsonValueKind.Array => "array",
		JsonValueKind.Null => "null",
		_ => "string"
	};
}

file sealed record AppSettingsImportPlan(
	Guid ApplicationId,
	ResourceScopeType ScopeType,
	Guid ScopeId,
	IReadOnlyList<AppSettingsImportNamespacePlan> Namespaces,
	IReadOnlyList<AppSettingsImportConfigItemPlan> ConfigItems);

file sealed record AppSettingsImportNamespacePlan(
	string Name,
	string Path,
	Guid? ExistingNamespaceId);

file sealed record AppSettingsImportConfigItemPlan(
	string NamespacePath,
	string Key,
	string FullPath,
	string ValueType,
	string ValueJson,
	bool IsSecret,
	Guid? ExistingNamespaceId,
	Guid? ExistingConfigItemId,
	Guid? ExistingDraftValueId,
	bool ExistingConfigItemIsSecret,
	string ExistingConfigItemValueType);

file sealed record ParsedAppSettingsConfigItem(
	string NamespacePath,
	string Key,
	string FullPath,
	string ValueType,
	string ValueJson,
	bool IsSecret);

public partial class Program;
