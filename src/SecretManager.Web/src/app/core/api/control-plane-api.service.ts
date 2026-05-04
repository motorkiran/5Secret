import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  AgentStatusResponse,
  ApplicationAssignmentResponse,
  ApplicationSummaryResponse,
  AuditEventDetailResponse,
  AuditEventSummaryResponse,
  BootstrapInstallationRequest,
  BootstrapInstallationResponse,
  BootstrapStatusResponse,
  ConfigItemSummaryResponse,
  CreateApplicationAssignmentRequest,
  CreateApplicationRequest,
  CreateConfigItemRequest,
  CreateDraftValueRequest,
  CreateEnvironmentRequest,
  CreateManagedNodeRequest,
  CreateNamespaceRequest,
  CreateNodeGroupRequest,
  CreatePublishRequest,
  CreatePublishResponse,
  CreateRollbackRequest,
  CurrentUserResponse,
  DraftValueResponse,
  EnvironmentSummaryResponse,
  EffectivePreviewResponse,
  LoginRequest,
  LoginResponse,
  ManagedNodeSummaryResponse,
  NamespaceSummaryResponse,
  NodeGroupSummaryResponse,
  PublishedVersionDiffResponse,
  PublishedVersionResponse,
  RollbackPublishedVersionResponse,
  UpdateApplicationRequest,
  UpdateConfigItemRequest,
  UpdateDraftValueRequest,
  UpdateEnvironmentRequest,
  UpdateManagedNodeRequest,
  UpdateNamespaceRequest,
  UpdateNodeGroupRequest
} from '../models/control-plane.models';

@Injectable({ providedIn: 'root' })
export class ControlPlaneApiService {
  private readonly httpClient = inject(HttpClient);

  getBootstrapStatus(): Promise<BootstrapStatusResponse> {
    return firstValueFrom(
      this.httpClient.get<BootstrapStatusResponse>('/api/v1/bootstrap/status', {
        withCredentials: true
      })
    );
  }

  bootstrap(request: BootstrapInstallationRequest): Promise<BootstrapInstallationResponse> {
    return firstValueFrom(
      this.httpClient.post<BootstrapInstallationResponse>('/api/v1/auth/bootstrap', request, {
        withCredentials: true
      })
    );
  }

  login(request: LoginRequest): Promise<LoginResponse> {
    return firstValueFrom(
      this.httpClient.post<LoginResponse>('/api/v1/auth/login', request, {
        withCredentials: true
      })
    );
  }

  logout(): Promise<void> {
    return firstValueFrom(
      this.httpClient.post<void>('/api/v1/auth/logout', null, {
        withCredentials: true
      })
    );
  }

  getCurrentUser(): Promise<CurrentUserResponse> {
    return firstValueFrom(
      this.httpClient.get<CurrentUserResponse>('/api/v1/auth/me', {
        withCredentials: true
      })
    );
  }

  listEnvironments(): Promise<EnvironmentSummaryResponse[]> {
    return firstValueFrom(
      this.httpClient.get<EnvironmentSummaryResponse[]>('/api/v1/environments', {
        withCredentials: true
      })
    );
  }

  createEnvironment(request: CreateEnvironmentRequest): Promise<EnvironmentSummaryResponse> {
    return firstValueFrom(
      this.httpClient.post<EnvironmentSummaryResponse>('/api/v1/environments', request, {
        withCredentials: true
      })
    );
  }

  updateEnvironment(
    environmentId: string,
    request: UpdateEnvironmentRequest
  ): Promise<EnvironmentSummaryResponse> {
    return firstValueFrom(
      this.httpClient.patch<EnvironmentSummaryResponse>(
        `/api/v1/environments/${environmentId}`,
        request,
        {
          withCredentials: true
        }
      )
    );
  }

  deleteEnvironment(environmentId: string): Promise<void> {
    return firstValueFrom(
      this.httpClient.delete<void>(`/api/v1/environments/${environmentId}`, {
        withCredentials: true
      })
    );
  }

  listNodeGroups(environmentId?: string | null): Promise<NodeGroupSummaryResponse[]> {
    return firstValueFrom(
      this.httpClient.get<NodeGroupSummaryResponse[]>('/api/v1/node-groups', {
        params: this.createParams({ environmentId }),
        withCredentials: true
      })
    );
  }

  createNodeGroup(request: CreateNodeGroupRequest): Promise<NodeGroupSummaryResponse> {
    return firstValueFrom(
      this.httpClient.post<NodeGroupSummaryResponse>('/api/v1/node-groups', request, {
        withCredentials: true
      })
    );
  }

  updateNodeGroup(nodeGroupId: string, request: UpdateNodeGroupRequest): Promise<NodeGroupSummaryResponse> {
    return firstValueFrom(
      this.httpClient.patch<NodeGroupSummaryResponse>(`/api/v1/node-groups/${nodeGroupId}`, request, {
        withCredentials: true
      })
    );
  }

  deleteNodeGroup(nodeGroupId: string): Promise<void> {
    return firstValueFrom(
      this.httpClient.delete<void>(`/api/v1/node-groups/${nodeGroupId}`, {
        withCredentials: true
      })
    );
  }

  listNodes(filters?: {
    environmentId?: string | null;
    nodeGroupId?: string | null;
  }): Promise<ManagedNodeSummaryResponse[]> {
    return firstValueFrom(
      this.httpClient.get<ManagedNodeSummaryResponse[]>('/api/v1/nodes', {
        params: this.createParams(filters),
        withCredentials: true
      })
    );
  }

  createNode(request: CreateManagedNodeRequest): Promise<ManagedNodeSummaryResponse> {
    return firstValueFrom(
      this.httpClient.post<ManagedNodeSummaryResponse>('/api/v1/nodes', request, {
        withCredentials: true
      })
    );
  }

  updateNode(nodeId: string, request: UpdateManagedNodeRequest): Promise<ManagedNodeSummaryResponse> {
    return firstValueFrom(
      this.httpClient.patch<ManagedNodeSummaryResponse>(`/api/v1/nodes/${nodeId}`, request, {
        withCredentials: true
      })
    );
  }

  deleteNode(nodeId: string): Promise<void> {
    return firstValueFrom(
      this.httpClient.delete<void>(`/api/v1/nodes/${nodeId}`, {
        withCredentials: true
      })
    );
  }

  listApplications(): Promise<ApplicationSummaryResponse[]> {
    return firstValueFrom(
      this.httpClient.get<ApplicationSummaryResponse[]>('/api/v1/applications', {
        withCredentials: true
      })
    );
  }

  createApplication(request: CreateApplicationRequest): Promise<ApplicationSummaryResponse> {
    return firstValueFrom(
      this.httpClient.post<ApplicationSummaryResponse>('/api/v1/applications', request, {
        withCredentials: true
      })
    );
  }

  updateApplication(
    applicationId: string,
    request: UpdateApplicationRequest
  ): Promise<ApplicationSummaryResponse> {
    return firstValueFrom(
      this.httpClient.patch<ApplicationSummaryResponse>(
        `/api/v1/applications/${applicationId}`,
        request,
        {
          withCredentials: true
        }
      )
    );
  }

  deleteApplication(applicationId: string): Promise<void> {
    return firstValueFrom(
      this.httpClient.delete<void>(`/api/v1/applications/${applicationId}`, {
        withCredentials: true
      })
    );
  }

  listNamespaces(applicationId?: string | null): Promise<NamespaceSummaryResponse[]> {
    return firstValueFrom(
      this.httpClient.get<NamespaceSummaryResponse[]>('/api/v1/namespaces', {
        params: this.createParams({ applicationId }),
        withCredentials: true
      })
    );
  }

  createNamespace(request: CreateNamespaceRequest): Promise<NamespaceSummaryResponse> {
    return firstValueFrom(
      this.httpClient.post<NamespaceSummaryResponse>('/api/v1/namespaces', request, {
        withCredentials: true
      })
    );
  }

  updateNamespace(
    namespaceId: string,
    request: UpdateNamespaceRequest
  ): Promise<NamespaceSummaryResponse> {
    return firstValueFrom(
      this.httpClient.patch<NamespaceSummaryResponse>(`/api/v1/namespaces/${namespaceId}`, request, {
        withCredentials: true
      })
    );
  }

  deleteNamespace(namespaceId: string): Promise<void> {
    return firstValueFrom(
      this.httpClient.delete<void>(`/api/v1/namespaces/${namespaceId}`, {
        withCredentials: true
      })
    );
  }

  listConfigItems(filters?: {
    applicationId?: string | null;
    namespaceId?: string | null;
  }): Promise<ConfigItemSummaryResponse[]> {
    return firstValueFrom(
      this.httpClient.get<ConfigItemSummaryResponse[]>('/api/v1/config-items', {
        params: this.createParams(filters),
        withCredentials: true
      })
    );
  }

  createConfigItem(request: CreateConfigItemRequest): Promise<ConfigItemSummaryResponse> {
    return firstValueFrom(
      this.httpClient.post<ConfigItemSummaryResponse>('/api/v1/config-items', request, {
        withCredentials: true
      })
    );
  }

  updateConfigItem(
    configItemId: string,
    request: UpdateConfigItemRequest
  ): Promise<ConfigItemSummaryResponse> {
    return firstValueFrom(
      this.httpClient.patch<ConfigItemSummaryResponse>(`/api/v1/config-items/${configItemId}`, request, {
        withCredentials: true
      })
    );
  }

  deleteConfigItem(configItemId: string): Promise<void> {
    return firstValueFrom(
      this.httpClient.delete<void>(`/api/v1/config-items/${configItemId}`, {
        withCredentials: true
      })
    );
  }

  listApplicationAssignments(filters?: {
    applicationId?: string | null;
    environmentId?: string | null;
  }): Promise<ApplicationAssignmentResponse[]> {
    return firstValueFrom(
      this.httpClient.get<ApplicationAssignmentResponse[]>('/api/v1/application-assignments', {
        params: this.createParams(filters),
        withCredentials: true
      })
    );
  }

  createApplicationAssignment(
    request: CreateApplicationAssignmentRequest
  ): Promise<ApplicationAssignmentResponse> {
    return firstValueFrom(
      this.httpClient.post<ApplicationAssignmentResponse>('/api/v1/application-assignments', request, {
        withCredentials: true
      })
    );
  }

  listDraftValues(filters?: {
    configItemId?: string | null;
    applicationId?: string | null;
    scopeType?: string | null;
    scopeId?: string | null;
  }): Promise<DraftValueResponse[]> {
    return firstValueFrom(
      this.httpClient.get<DraftValueResponse[]>('/api/v1/draft-values', {
        params: this.createParams(filters),
        withCredentials: true
      })
    );
  }

  createDraftValue(request: CreateDraftValueRequest): Promise<DraftValueResponse> {
    return firstValueFrom(
      this.httpClient.post<DraftValueResponse>('/api/v1/draft-values', request, {
        withCredentials: true
      })
    );
  }

  updateDraftValue(
    draftValueId: string,
    request: UpdateDraftValueRequest
  ): Promise<DraftValueResponse> {
    return firstValueFrom(
      this.httpClient.patch<DraftValueResponse>(`/api/v1/draft-values/${draftValueId}`, request, {
        withCredentials: true
      })
    );
  }

  deleteDraftValue(draftValueId: string): Promise<void> {
    return firstValueFrom(
      this.httpClient.delete<void>(`/api/v1/draft-values/${draftValueId}`, {
        withCredentials: true
      })
    );
  }

  getEffectivePreview(filters: {
    applicationId: string;
    environmentId: string;
    managedNodeId: string;
    namespaceId?: string | null;
  }): Promise<EffectivePreviewResponse> {
    return firstValueFrom(
      this.httpClient.get<EffectivePreviewResponse>('/api/v1/effective-snapshots/preview', {
        params: this.createParams(filters),
        withCredentials: true
      })
    );
  }

  listPublishedVersions(filters?: {
    applicationId?: string | null;
    environmentId?: string | null;
  }): Promise<PublishedVersionResponse[]> {
    return firstValueFrom(
      this.httpClient.get<PublishedVersionResponse[]>('/api/v1/published-versions', {
        params: this.createParams(filters),
        withCredentials: true
      })
    );
  }

  createPublish(request: CreatePublishRequest): Promise<CreatePublishResponse> {
    return firstValueFrom(
      this.httpClient.post<CreatePublishResponse>('/api/v1/publishes', request, {
        withCredentials: true
      })
    );
  }

  getPublishedVersionDiff(
    versionId: string,
    compareToVersionId?: string | null
  ): Promise<PublishedVersionDiffResponse> {
    return firstValueFrom(
      this.httpClient.get<PublishedVersionDiffResponse>(`/api/v1/published-versions/${versionId}/diff`, {
        params: this.createParams({ compareToVersionId }),
        withCredentials: true
      })
    );
  }

  rollbackPublishedVersion(
    versionId: string,
    request: CreateRollbackRequest
  ): Promise<RollbackPublishedVersionResponse> {
    return firstValueFrom(
      this.httpClient.post<RollbackPublishedVersionResponse>(
        `/api/v1/published-versions/${versionId}/rollback`,
        request,
        {
          withCredentials: true
        }
      )
    );
  }

  listAgents(environmentId?: string | null): Promise<AgentStatusResponse[]> {
    return firstValueFrom(
      this.httpClient.get<AgentStatusResponse[]>('/api/v1/agents', {
        params: this.createParams({ environmentId }),
        withCredentials: true
      })
    );
  }

  getAgentStatus(agentId: string): Promise<AgentStatusResponse> {
    return firstValueFrom(
      this.httpClient.get<AgentStatusResponse>(`/api/v1/agents/${agentId}/status`, {
        withCredentials: true
      })
    );
  }

  listAuditEvents(take = 100): Promise<AuditEventSummaryResponse[]> {
    return firstValueFrom(
      this.httpClient.get<AuditEventSummaryResponse[]>('/api/v1/audit-events', {
        params: this.createParams({ take: take.toString() }),
        withCredentials: true
      })
    );
  }

  getAuditEvent(eventId: string): Promise<AuditEventDetailResponse> {
    return firstValueFrom(
      this.httpClient.get<AuditEventDetailResponse>(`/api/v1/audit-events/${eventId}`, {
        withCredentials: true
      })
    );
  }

  private createParams(
    filters?: Record<string, string | null | undefined>
  ): HttpParams | undefined {
    if (!filters) {
      return undefined;
    }

    let params = new HttpParams();
    for (const [key, value] of Object.entries(filters)) {
      if (value) {
        params = params.set(key, value);
      }
    }

    return params.keys().length > 0 ? params : undefined;
  }
}