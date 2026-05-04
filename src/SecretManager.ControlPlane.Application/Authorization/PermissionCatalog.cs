namespace SecretManager.ControlPlane.Application.Authorization;

public static class PermissionCatalog
{
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
    public const string EnvironmentsRead = "environments.read";
    public const string EnvironmentsWrite = "environments.write";
    public const string NodeGroupsRead = "nodeGroups.read";
    public const string NodeGroupsWrite = "nodeGroups.write";
    public const string NodesRead = "nodes.read";
    public const string NodesWrite = "nodes.write";
    public const string ApplicationsRead = "applications.read";
    public const string ApplicationsWrite = "applications.write";
    public const string NamespacesRead = "namespaces.read";
    public const string NamespacesWrite = "namespaces.write";
    public const string ConfigReadMasked = "config.readMasked";
    public const string ConfigRevealSecret = "config.revealSecret";
    public const string ConfigWriteDraft = "config.writeDraft";
    public const string ConfigDeleteDraft = "config.deleteDraft";
    public const string ConfigPublish = "config.publish";
    public const string ConfigRollback = "config.rollback";
    public const string AuditRead = "audit.read";
    public const string AgentsRead = "agents.read";
    public const string AgentsWrite = "agents.write";

    public static IReadOnlyList<string> All { get; } =
    [
        UsersRead,
        UsersWrite,
        RolesRead,
        RolesWrite,
        EnvironmentsRead,
        EnvironmentsWrite,
        NodeGroupsRead,
        NodeGroupsWrite,
        NodesRead,
        NodesWrite,
        ApplicationsRead,
        ApplicationsWrite,
        NamespacesRead,
        NamespacesWrite,
        ConfigReadMasked,
        ConfigRevealSecret,
        ConfigWriteDraft,
        ConfigDeleteDraft,
        ConfigPublish,
        ConfigRollback,
        AuditRead,
        AgentsRead,
        AgentsWrite
    ];
}