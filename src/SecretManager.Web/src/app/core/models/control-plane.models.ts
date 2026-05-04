export interface BootstrapStatusResponse {
  isInitialized: boolean;
  installationName: string | null;
}

export interface BootstrapInstallationRequest {
  installationName: string;
  ownerUsername: string;
  ownerDisplayName: string;
  password: string;
}

export interface BootstrapInstallationResponse {
  installationId: string;
  ownerUserId: string;
  installationName: string;
  ownerUsername: string;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  userId: string;
  username: string;
  displayName: string;
  role: string;
}

export interface CurrentUserResponse {
  userId: string;
  username: string;
  displayName: string;
  role: string;
  isAuthenticated: boolean;
}

export interface EnvironmentSummaryResponse {
  environmentId: string;
  name: string;
  slug: string;
  description: string;
  isProtected: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateEnvironmentRequest {
  name: string;
  slug?: string | null;
  description: string;
  isProtected: boolean;
}

export interface UpdateEnvironmentRequest extends CreateEnvironmentRequest {}

export interface NodeGroupSummaryResponse {
  nodeGroupId: string;
  environmentId: string;
  name: string;
  slug: string;
  description: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateNodeGroupRequest {
  environmentId: string;
  name: string;
  slug?: string | null;
  description: string;
}

export interface UpdateNodeGroupRequest {
  name: string;
  slug?: string | null;
  description: string;
}

export interface ManagedNodeSummaryResponse {
  nodeId: string;
  environmentId: string;
  nodeGroupId: string | null;
  name: string;
  hostname: string;
  platform: string;
  status: string;
  lastSeenAtUtc: string | null;
  agentVersion: string;
  rolloutPolicyDefault: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateManagedNodeRequest {
  environmentId: string;
  nodeGroupId: string | null;
  name: string;
  hostname: string;
  platform: string;
  status: string;
  lastSeenAtUtc: string | null;
  agentVersion: string;
  rolloutPolicyDefault: string;
}

export interface UpdateManagedNodeRequest {
  nodeGroupId: string | null;
  name: string;
  hostname: string;
  platform: string;
  status: string;
  lastSeenAtUtc: string | null;
  agentVersion: string;
  rolloutPolicyDefault: string;
}

export interface ApplicationSummaryResponse {
  applicationId: string;
  name: string;
  slug: string;
  description: string;
  defaultIntegrationMode: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateApplicationRequest {
  name: string;
  slug?: string | null;
  description: string;
  defaultIntegrationMode: string;
}

export interface UpdateApplicationRequest extends CreateApplicationRequest {}

export interface NamespaceSummaryResponse {
  namespaceId: string;
  applicationId: string;
  name: string;
  path: string;
  description: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateNamespaceRequest {
  applicationId: string;
  name: string;
  path?: string | null;
  description: string;
}

export interface UpdateNamespaceRequest {
  name: string;
  path?: string | null;
  description: string;
}

export interface ConfigItemSummaryResponse {
  configItemId: string;
  applicationId: string;
  namespaceId: string;
  key: string;
  fullPath: string;
  valueType: string;
  isSecret: boolean;
  isRequired: boolean;
  defaultRolloutPolicy: string;
  validationSchemaJson: string;
  description: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateConfigItemRequest {
  namespaceId: string;
  key: string;
  valueType: string;
  isSecret: boolean;
  isRequired: boolean;
  defaultRolloutPolicy: string;
  validationSchemaJson: string;
  description: string;
}

export interface UpdateConfigItemRequest extends CreateConfigItemRequest {}

export interface ApplicationAssignmentResponse {
  assignmentId: string;
  applicationId: string;
  environmentId: string;
  nodeGroupId: string | null;
  managedNodeId: string | null;
  enabled: boolean;
  createdAtUtc: string;
}

export interface CreateApplicationAssignmentRequest {
  applicationId: string;
  environmentId: string;
  nodeGroupId: string | null;
  managedNodeId: string | null;
  enabled: boolean;
}

export interface CreateDraftValueRequest {
  configItemId: string;
  scopeType: string;
  scopeId: string;
  valueJson: string;
  changeNote: string;
}

export interface UpdateDraftValueRequest {
  valueJson: string;
  changeNote: string;
}

export interface DraftValueResponse {
  draftValueId: string;
  configItemId: string;
  scopeType: string;
  scopeId: string;
  valueJson: string | null;
  isSecret: boolean;
  isValueMasked: boolean;
  changeNote: string;
  updatedByUserId: string | null;
  updatedAtUtc: string;
}

export interface EffectivePreviewItemResponse {
  draftValueId: string;
  configItemId: string;
  fullPath: string;
  valueType: string;
  valueJson: string | null;
  isSecret: boolean;
  isValueMasked: boolean;
  sourceScopeType: string;
  sourceScopeId: string;
  updatedAtUtc: string;
}

export interface EffectivePreviewResponse {
  applicationId: string;
  environmentId: string;
  managedNodeId: string;
  nodeGroupId: string | null;
  itemCount: number;
  items: EffectivePreviewItemResponse[];
}

export interface CreatePublishRequest {
  applicationId: string;
  environmentId: string;
  changeSummary: string;
  rolloutPolicy: string;
}

export interface PublishOperationResponse {
  publishOperationId: string;
  environmentId: string;
  applicationId: string;
  initiatedByUserId: string | null;
  changeSummary: string;
  status: string;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

export interface PublishedVersionResponse {
  publishedVersionId: string;
  publishOperationId: string;
  environmentId: string;
  applicationId: string;
  versionNumber: number;
  rolloutPolicy: string;
  contentHash: string;
  publishedByUserId: string | null;
  publishedAtUtc: string;
  supersedesVersionId: string | null;
}

export interface CreatePublishResponse {
  publishOperation: PublishOperationResponse;
  publishedVersion: PublishedVersionResponse;
}

export interface PublishedVersionDiffItemResponse {
  previousDraftValueId: string | null;
  currentDraftValueId: string | null;
  configItemId: string;
  fullPath: string;
  scopeType: string;
  scopeId: string;
  changeType: string;
  previousValueJson: string | null;
  currentValueJson: string | null;
  isSecret: boolean;
  isValueMasked: boolean;
}

export interface PublishedVersionDiffResponse {
  versionId: string;
  compareToVersionId: string;
  currentRolloutPolicy: string;
  compareToRolloutPolicy: string;
  rolloutPolicyChanged: boolean;
  changeCount: number;
  changes: PublishedVersionDiffItemResponse[];
}

export interface CreateRollbackRequest {
  changeSummary: string;
}

export interface RollbackPublishedVersionResponse {
  sourcePublishedVersionId: string;
  publishOperation: PublishOperationResponse;
  publishedVersion: PublishedVersionResponse;
}

export interface AgentStatusResponse {
  agentId: string;
  managedNodeId: string;
  environmentId: string;
  nodeGroupId: string | null;
  hostname: string;
  agentVersion: string;
  lastSeenAtUtc: string | null;
  healthStatus: string;
  currentPublishedVersionId: string | null;
  currentVersionNumber: number | null;
}

export interface AuditEventSummaryResponse {
  eventId: string;
  action: string;
  outcome: string;
  targetType: string;
  targetIdentifier: string;
  targetDisplayName: string;
  actorUserId: string | null;
  actorUsername: string | null;
  occurredAtUtc: string;
  correlationId: string;
}

export interface AuditEventDetailResponse {
  eventId: string;
  action: string;
  outcome: string;
  targetType: string;
  targetIdentifier: string;
  targetDisplayName: string;
  actorUserId: string | null;
  actorUsername: string | null;
  occurredAtUtc: string;
  correlationId: string;
  remoteIpAddress: string | null;
  detailsJson: string | null;
}