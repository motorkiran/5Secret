using SecretManager.Domain.Authorization;

namespace SecretManager.Domain.Catalog;

public sealed class ConfigItemDefinition
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    public Guid NamespaceId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public string ValueType { get; set; } = string.Empty;

    public bool IsSecret { get; set; }

    public bool IsRequired { get; set; }

    public string DefaultRolloutPolicy { get; set; } = string.Empty;

    public string ValidationSchemaJson { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class DraftValue
{
    public Guid Id { get; set; }

    public Guid ConfigItemId { get; set; }

    public ResourceScopeType ScopeType { get; set; }

    public Guid ScopeId { get; set; }

    public string ValueJson { get; set; } = string.Empty;

    public bool IsSecret { get; set; }

    public string ChangeNote { get; set; } = string.Empty;

    public Guid? UpdatedByUserId { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}