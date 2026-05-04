namespace SecretManager.Domain.Authorization;

public enum ResourceScopeType
{
    Installation = 1,
    Environment = 2,
    NodeGroup = 3,
    ManagedNode = 4,
    Application = 5,
    Namespace = 6,
    ConfigItem = 7,
    EmergencyOverride = 8
}