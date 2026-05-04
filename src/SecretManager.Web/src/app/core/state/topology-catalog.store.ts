import { inject, Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ControlPlaneApiService } from '../api/control-plane-api.service';
import {
  ApplicationAssignmentResponse,
  ApplicationSummaryResponse,
  ConfigItemSummaryResponse,
  CreateApplicationAssignmentRequest,
  CreateApplicationRequest,
  CreateConfigItemRequest,
  CreateEnvironmentRequest,
  CreateManagedNodeRequest,
  CreateNamespaceRequest,
  CreateNodeGroupRequest,
  EnvironmentSummaryResponse,
  ManagedNodeSummaryResponse,
  NamespaceSummaryResponse,
  NodeGroupSummaryResponse,
  UpdateApplicationRequest,
  UpdateConfigItemRequest,
  UpdateEnvironmentRequest,
  UpdateManagedNodeRequest,
  UpdateNamespaceRequest,
  UpdateNodeGroupRequest
} from '../models/control-plane.models';

@Injectable({ providedIn: 'root' })
export class TopologyCatalogStore {
  private readonly api = inject(ControlPlaneApiService);

  readonly environments = signal<EnvironmentSummaryResponse[]>([]);
  readonly nodeGroups = signal<NodeGroupSummaryResponse[]>([]);
  readonly nodes = signal<ManagedNodeSummaryResponse[]>([]);
  readonly applications = signal<ApplicationSummaryResponse[]>([]);
  readonly namespaces = signal<NamespaceSummaryResponse[]>([]);
  readonly configItems = signal<ConfigItemSummaryResponse[]>([]);
  readonly assignments = signal<ApplicationAssignmentResponse[]>([]);
  readonly loading = signal(false);
  readonly activeOperation = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  private initialized = false;

  async initialize(): Promise<void> {
    if (this.initialized) {
      return;
    }

    await this.refreshAll();
    this.initialized = true;
  }

  clearError(): void {
    this.errorMessage.set(null);
  }

  async refreshAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [environments, nodeGroups, nodes, applications, namespaces, configItems, assignments] =
        await Promise.all([
          this.api.listEnvironments(),
          this.api.listNodeGroups(),
          this.api.listNodes(),
          this.api.listApplications(),
          this.api.listNamespaces(),
          this.api.listConfigItems(),
          this.api.listApplicationAssignments()
        ]);

      this.environments.set(environments);
      this.nodeGroups.set(nodeGroups);
      this.nodes.set(nodes);
      this.applications.set(applications);
      this.namespaces.set(namespaces);
      this.configItems.set(configItems);
      this.assignments.set(assignments);
    } catch (error) {
      this.errorMessage.set(this.describeError(error));
      throw error;
    } finally {
      this.loading.set(false);
    }
  }

  createEnvironment(request: CreateEnvironmentRequest): Promise<void> {
    return this.run('create-environment', async () => {
      await this.api.createEnvironment(request);
      await this.refreshAll();
    });
  }

  updateEnvironment(environmentId: string, request: UpdateEnvironmentRequest): Promise<void> {
    return this.run('update-environment', async () => {
      await this.api.updateEnvironment(environmentId, request);
      await this.refreshAll();
    });
  }

  deleteEnvironment(environmentId: string): Promise<void> {
    return this.run('delete-environment', async () => {
      await this.api.deleteEnvironment(environmentId);
      await this.refreshAll();
    });
  }

  createNodeGroup(request: CreateNodeGroupRequest): Promise<void> {
    return this.run('create-node-group', async () => {
      await this.api.createNodeGroup(request);
      await this.refreshAll();
    });
  }

  updateNodeGroup(nodeGroupId: string, request: UpdateNodeGroupRequest): Promise<void> {
    return this.run('update-node-group', async () => {
      await this.api.updateNodeGroup(nodeGroupId, request);
      await this.refreshAll();
    });
  }

  deleteNodeGroup(nodeGroupId: string): Promise<void> {
    return this.run('delete-node-group', async () => {
      await this.api.deleteNodeGroup(nodeGroupId);
      await this.refreshAll();
    });
  }

  createNode(request: CreateManagedNodeRequest): Promise<void> {
    return this.run('create-node', async () => {
      await this.api.createNode(request);
      await this.refreshAll();
    });
  }

  updateNode(nodeId: string, request: UpdateManagedNodeRequest): Promise<void> {
    return this.run('update-node', async () => {
      await this.api.updateNode(nodeId, request);
      await this.refreshAll();
    });
  }

  deleteNode(nodeId: string): Promise<void> {
    return this.run('delete-node', async () => {
      await this.api.deleteNode(nodeId);
      await this.refreshAll();
    });
  }

  createApplication(request: CreateApplicationRequest): Promise<void> {
    return this.run('create-application', async () => {
      await this.api.createApplication(request);
      await this.refreshAll();
    });
  }

  updateApplication(applicationId: string, request: UpdateApplicationRequest): Promise<void> {
    return this.run('update-application', async () => {
      await this.api.updateApplication(applicationId, request);
      await this.refreshAll();
    });
  }

  deleteApplication(applicationId: string): Promise<void> {
    return this.run('delete-application', async () => {
      await this.api.deleteApplication(applicationId);
      await this.refreshAll();
    });
  }

  createNamespace(request: CreateNamespaceRequest): Promise<void> {
    return this.run('create-namespace', async () => {
      await this.api.createNamespace(request);
      await this.refreshAll();
    });
  }

  updateNamespace(namespaceId: string, request: UpdateNamespaceRequest): Promise<void> {
    return this.run('update-namespace', async () => {
      await this.api.updateNamespace(namespaceId, request);
      await this.refreshAll();
    });
  }

  deleteNamespace(namespaceId: string): Promise<void> {
    return this.run('delete-namespace', async () => {
      await this.api.deleteNamespace(namespaceId);
      await this.refreshAll();
    });
  }

  createConfigItem(request: CreateConfigItemRequest): Promise<void> {
    return this.run('create-config-item', async () => {
      await this.api.createConfigItem(request);
      await this.refreshAll();
    });
  }

  updateConfigItem(configItemId: string, request: UpdateConfigItemRequest): Promise<void> {
    return this.run('update-config-item', async () => {
      await this.api.updateConfigItem(configItemId, request);
      await this.refreshAll();
    });
  }

  deleteConfigItem(configItemId: string): Promise<void> {
    return this.run('delete-config-item', async () => {
      await this.api.deleteConfigItem(configItemId);
      await this.refreshAll();
    });
  }

  createApplicationAssignment(request: CreateApplicationAssignmentRequest): Promise<void> {
    return this.run('create-assignment', async () => {
      await this.api.createApplicationAssignment(request);
      await this.refreshAll();
    });
  }

  private async run(operationName: string, action: () => Promise<void>): Promise<void> {
    this.activeOperation.set(operationName);
    this.errorMessage.set(null);

    try {
      await action();
    } catch (error) {
      this.errorMessage.set(this.describeError(error));
      throw error;
    } finally {
      this.activeOperation.set(null);
    }
  }

  private describeError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (typeof error.error === 'object' && error.error !== null) {
        const problem = error.error as {
          detail?: string;
          title?: string;
          errors?: Record<string, string[]>;
        };
        const validationMessages = problem.errors
          ? Object.values(problem.errors).flat().join(' ')
          : null;

        return validationMessages || problem.detail || problem.title || 'The request failed.';
      }

      if (typeof error.error === 'string' && error.error.length > 0) {
        return error.error;
      }

      return error.message || 'The request failed.';
    }

    return 'The request could not be completed.';
  }
}