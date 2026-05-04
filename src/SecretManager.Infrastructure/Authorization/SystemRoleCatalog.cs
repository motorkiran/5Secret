using SecretManager.ControlPlane.Application.Authorization;

namespace SecretManager.Infrastructure.Authorization;

internal static class SystemRoleCatalog
{
    internal const string BootstrapOwner = "BootstrapOwner";

    internal static IReadOnlyList<SystemRoleDefinition> Roles { get; } =
    [
        new(
            BootstrapOwner,
            "Full installation-wide access for the installation owner.",
            PermissionCatalog.All),
        new(
            "SecurityAdministrator",
            "Manages users, roles, secret reveal permissions, and audit visibility.",
            [
                PermissionCatalog.UsersRead,
                PermissionCatalog.UsersWrite,
                PermissionCatalog.RolesRead,
                PermissionCatalog.RolesWrite,
                PermissionCatalog.ConfigRevealSecret,
                PermissionCatalog.AuditRead,
                PermissionCatalog.AgentsRead,
                PermissionCatalog.AgentsWrite
            ]),
        new(
            "ConfigurationAdministrator",
            "Manages environments, topology, draft changes, publish, and rollback flows.",
            [
                PermissionCatalog.EnvironmentsRead,
                PermissionCatalog.EnvironmentsWrite,
                PermissionCatalog.NodeGroupsRead,
                PermissionCatalog.NodeGroupsWrite,
                PermissionCatalog.NodesRead,
                PermissionCatalog.NodesWrite,
                PermissionCatalog.ApplicationsRead,
                PermissionCatalog.ApplicationsWrite,
                PermissionCatalog.NamespacesRead,
                PermissionCatalog.NamespacesWrite,
                PermissionCatalog.ConfigReadMasked,
                PermissionCatalog.ConfigWriteDraft,
                PermissionCatalog.ConfigDeleteDraft,
                PermissionCatalog.ConfigPublish,
                PermissionCatalog.ConfigRollback,
                PermissionCatalog.AgentsRead
            ]),
        new(
            "EnvironmentOperator",
            "Works within assigned scopes on configuration metadata and drafts.",
            [
                PermissionCatalog.EnvironmentsRead,
                PermissionCatalog.NodeGroupsRead,
                PermissionCatalog.NodesRead,
                PermissionCatalog.ApplicationsRead,
                PermissionCatalog.NamespacesRead,
                PermissionCatalog.ConfigReadMasked,
                PermissionCatalog.ConfigWriteDraft,
                PermissionCatalog.ConfigDeleteDraft
            ]),
        new(
            "Auditor",
            "Read-only access to metadata and audit history without secret reveal.",
            [
                PermissionCatalog.EnvironmentsRead,
                PermissionCatalog.NodeGroupsRead,
                PermissionCatalog.NodesRead,
                PermissionCatalog.ApplicationsRead,
                PermissionCatalog.NamespacesRead,
                PermissionCatalog.ConfigReadMasked,
                PermissionCatalog.AuditRead
            ]),
        new(
            "Reader",
            "Masked read-only access to assigned scopes.",
            [
                PermissionCatalog.EnvironmentsRead,
                PermissionCatalog.NodeGroupsRead,
                PermissionCatalog.NodesRead,
                PermissionCatalog.ApplicationsRead,
                PermissionCatalog.NamespacesRead,
                PermissionCatalog.ConfigReadMasked
            ])
    ];

    internal sealed record SystemRoleDefinition(
        string Name,
        string Description,
        IReadOnlyCollection<string> Permissions);
}