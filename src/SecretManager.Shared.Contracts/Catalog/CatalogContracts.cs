using System.ComponentModel.DataAnnotations;

namespace SecretManager.Shared.Contracts.Catalog;

public sealed class CreateApplicationRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Slug { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(64)]
    public string DefaultIntegrationMode { get; set; } = string.Empty;
}

public sealed class UpdateApplicationRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Slug { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(64)]
    public string DefaultIntegrationMode { get; set; } = string.Empty;
}

public sealed record ApplicationSummaryResponse(
    Guid ApplicationId,
    string Name,
    string Slug,
    string Description,
    string DefaultIntegrationMode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class CreateNamespaceRequest
{
    public Guid ApplicationId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Path { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;
}

public sealed class UpdateNamespaceRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Path { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;
}

public sealed record NamespaceSummaryResponse(
    Guid NamespaceId,
    Guid ApplicationId,
    string Name,
    string Path,
    string Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class CreateApplicationAssignmentRequest
{
    public Guid ApplicationId { get; set; }

    public Guid EnvironmentId { get; set; }

    public Guid? NodeGroupId { get; set; }

    public Guid? ManagedNodeId { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed record ApplicationAssignmentResponse(
    Guid AssignmentId,
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid? NodeGroupId,
    Guid? ManagedNodeId,
    bool Enabled,
    DateTimeOffset CreatedAtUtc);

public sealed class CreateConfigItemRequest
{
    public Guid NamespaceId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ValueType { get; set; } = string.Empty;

    public bool IsSecret { get; set; }

    public bool IsRequired { get; set; }

    [MaxLength(64)]
    public string DefaultRolloutPolicy { get; set; } = string.Empty;

    public string ValidationSchemaJson { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;
}

public sealed class UpdateConfigItemRequest
{
    public Guid NamespaceId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ValueType { get; set; } = string.Empty;

    public bool IsSecret { get; set; }

    public bool IsRequired { get; set; }

    [MaxLength(64)]
    public string DefaultRolloutPolicy { get; set; } = string.Empty;

    public string ValidationSchemaJson { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;
}

public sealed record ConfigItemSummaryResponse(
    Guid ConfigItemId,
    Guid ApplicationId,
    Guid NamespaceId,
    string Key,
    string FullPath,
    string ValueType,
    bool IsSecret,
    bool IsRequired,
    string DefaultRolloutPolicy,
    string ValidationSchemaJson,
    string Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class CreateDraftValueRequest
{
    public Guid ConfigItemId { get; set; }

    public Guid ScopeId { get; set; }

    [Required]
    [MaxLength(64)]
    public string ScopeType { get; set; } = string.Empty;

    [Required]
    public string ValueJson { get; set; } = string.Empty;

    [MaxLength(512)]
    public string ChangeNote { get; set; } = string.Empty;
}

public sealed class UpdateDraftValueRequest
{
    [Required]
    public string ValueJson { get; set; } = string.Empty;

    [MaxLength(512)]
    public string ChangeNote { get; set; } = string.Empty;
}

public sealed record DraftValueResponse(
    Guid DraftValueId,
    Guid ConfigItemId,
    string ScopeType,
    Guid ScopeId,
    string? ValueJson,
    bool IsSecret,
    bool IsValueMasked,
    string ChangeNote,
    Guid? UpdatedByUserId,
    DateTimeOffset UpdatedAtUtc);

public sealed record EffectivePreviewItemResponse(
    Guid DraftValueId,
    Guid ConfigItemId,
    string FullPath,
    string ValueType,
    string? ValueJson,
    bool IsSecret,
    bool IsValueMasked,
    string SourceScopeType,
    Guid SourceScopeId,
    DateTimeOffset UpdatedAtUtc);

public sealed record EffectivePreviewResponse(
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid ManagedNodeId,
    Guid? NodeGroupId,
    int ItemCount,
    IReadOnlyList<EffectivePreviewItemResponse> Items);

public sealed class AppSettingsImportPreviewRequest
{
    public Guid ApplicationId { get; set; }

    public Guid ScopeId { get; set; }

    [Required]
    [MaxLength(64)]
    public string ScopeType { get; set; } = string.Empty;

    [Required]
    public string JsonPayload { get; set; } = string.Empty;

    public List<string> SecretFullPaths { get; set; } = [];
}

public sealed class AppSettingsImportApplyRequest
{
    public Guid ApplicationId { get; set; }

    public Guid ScopeId { get; set; }

    [Required]
    [MaxLength(64)]
    public string ScopeType { get; set; } = string.Empty;

    [Required]
    public string JsonPayload { get; set; } = string.Empty;

    public List<string> SecretFullPaths { get; set; } = [];

    [MaxLength(512)]
    public string ChangeNote { get; set; } = string.Empty;
}

public sealed record AppSettingsImportNamespacePreviewResponse(
    string Name,
    string Path,
    bool Exists);

public sealed record AppSettingsImportConfigItemPreviewResponse(
    string NamespacePath,
    string Key,
    string FullPath,
    string ValueType,
    bool IsSecret,
    bool NamespaceExists,
    Guid? ExistingConfigItemId,
    bool HasExistingDraftValue);

public sealed record AppSettingsImportPreviewResponse(
    Guid ApplicationId,
    string ScopeType,
    Guid ScopeId,
    IReadOnlyList<AppSettingsImportNamespacePreviewResponse> Namespaces,
    IReadOnlyList<AppSettingsImportConfigItemPreviewResponse> ConfigItems,
    int ConflictCount);

public sealed record AppSettingsImportApplyResponse(
    Guid ApplicationId,
    string ScopeType,
    Guid ScopeId,
    int NamespaceCount,
    int ConfigItemCount,
    int DraftValueCount,
    int CreatedNamespaceCount,
    int CreatedConfigItemCount,
    int UpdatedConfigItemCount,
    int CreatedDraftValueCount,
    int UpdatedDraftValueCount);

public sealed class CreatePublishRequest
{
    public Guid ApplicationId { get; set; }

    public Guid EnvironmentId { get; set; }

    [Required]
    [MaxLength(512)]
    public string ChangeSummary { get; set; } = string.Empty;

    [MaxLength(64)]
    public string RolloutPolicy { get; set; } = string.Empty;
}

public sealed record PublishOperationResponse(
    Guid PublishOperationId,
    Guid EnvironmentId,
    Guid ApplicationId,
    Guid? InitiatedByUserId,
    string ChangeSummary,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record PublishedVersionResponse(
    Guid PublishedVersionId,
    Guid PublishOperationId,
    Guid EnvironmentId,
    Guid ApplicationId,
    int VersionNumber,
    string RolloutPolicy,
    string ContentHash,
    Guid? PublishedByUserId,
    DateTimeOffset PublishedAtUtc,
    Guid? SupersedesVersionId);

public sealed record CreatePublishResponse(
    PublishOperationResponse PublishOperation,
    PublishedVersionResponse PublishedVersion);

public sealed class CreateRollbackRequest
{
    [MaxLength(512)]
    public string ChangeSummary { get; set; } = string.Empty;
}

public sealed record PublishedVersionDiffItemResponse(
    Guid? PreviousDraftValueId,
    Guid? CurrentDraftValueId,
    Guid ConfigItemId,
    string FullPath,
    string ScopeType,
    Guid ScopeId,
    string ChangeType,
    string? PreviousValueJson,
    string? CurrentValueJson,
    bool IsSecret,
    bool IsValueMasked);

public sealed record PublishedVersionDiffResponse(
    Guid VersionId,
    Guid CompareToVersionId,
    string CurrentRolloutPolicy,
    string CompareToRolloutPolicy,
    bool RolloutPolicyChanged,
    int ChangeCount,
    IReadOnlyList<PublishedVersionDiffItemResponse> Changes);

public sealed record RollbackPublishedVersionResponse(
    Guid SourcePublishedVersionId,
    PublishOperationResponse PublishOperation,
    PublishedVersionResponse PublishedVersion);