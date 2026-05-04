import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TopologyCatalogStore } from '../../core/state/topology-catalog.store';
import {
  ApplicationSummaryResponse,
  ConfigItemSummaryResponse,
  CreateApplicationAssignmentRequest,
  CreateApplicationRequest,
  CreateConfigItemRequest,
  CreateNamespaceRequest,
  ManagedNodeSummaryResponse,
  NamespaceSummaryResponse,
  NodeGroupSummaryResponse,
  UpdateApplicationRequest,
  UpdateConfigItemRequest,
  UpdateNamespaceRequest
} from '../../core/models/control-plane.models';

@Component({
  selector: 'app-catalog-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './catalog-page.component.html',
  styleUrl: './catalog-page.component.scss'
})
export class CatalogPageComponent implements OnInit {
  protected readonly store = inject(TopologyCatalogStore);

  protected readonly selectedApplicationId = signal<string | null>(null);
  protected readonly selectedNamespaceId = signal<string | null>(null);
  protected readonly selectedConfigItemId = signal<string | null>(null);

  protected readonly selectedApplication = computed(
    () =>
      this.store
        .applications()
        .find((application) => application.applicationId === this.selectedApplicationId()) ?? null
  );

  protected readonly namespacesForApplication = computed(() => {
    const applicationId = this.selectedApplicationId();
    return applicationId
      ? this.store.namespaces().filter((catalogNamespace) => catalogNamespace.applicationId === applicationId)
      : [];
  });

  protected readonly selectedNamespace = computed(
    () =>
      this.namespacesForApplication().find(
        (catalogNamespace) => catalogNamespace.namespaceId === this.selectedNamespaceId()
      ) ?? null
  );

  protected readonly configItemsForApplication = computed(() => {
    const applicationId = this.selectedApplicationId();
    return applicationId
      ? this.store.configItems().filter((configItem) => configItem.applicationId === applicationId)
      : [];
  });

  protected readonly selectedConfigItem = computed(
    () =>
      this.configItemsForApplication().find(
        (configItem) => configItem.configItemId === this.selectedConfigItemId()
      ) ?? null
  );

  protected readonly assignmentsForApplication = computed(() => {
    const applicationId = this.selectedApplicationId();
    return applicationId
      ? this.store.assignments().filter((assignment) => assignment.applicationId === applicationId)
      : [];
  });

  protected applicationForm: CreateApplicationRequest = this.createApplicationDraft();
  protected namespaceForm: CreateNamespaceRequest = this.createNamespaceDraft();
  protected configItemForm: CreateConfigItemRequest = this.createConfigItemDraft();

  protected applicationEditingId: string | null = null;
  protected namespaceEditingId: string | null = null;
  protected configItemEditingId: string | null = null;

  protected assignmentEnvironmentId = '';
  protected assignmentTargetMode: 'nodeGroup' | 'managedNode' = 'nodeGroup';
  protected assignmentTargetId = '';
  protected assignmentEnabled = true;

  constructor() {
    effect(() => {
      const applications = this.store.applications();
      const selected = this.selectedApplicationId();
      if (!applications.some((application) => application.applicationId === selected)) {
        const nextApplication = applications[0] ?? null;
        this.selectedApplicationId.set(nextApplication?.applicationId ?? null);
        if (nextApplication) {
          this.loadApplication(nextApplication);
        } else {
          this.resetApplicationForm();
        }
      }
    });

    effect(() => {
      const namespaces = this.namespacesForApplication();
      const selected = this.selectedNamespaceId();
      if (!namespaces.some((catalogNamespace) => catalogNamespace.namespaceId === selected)) {
        const nextNamespace = namespaces[0] ?? null;
        this.selectedNamespaceId.set(nextNamespace?.namespaceId ?? null);
        if (nextNamespace) {
          this.loadNamespace(nextNamespace);
        } else {
          this.resetNamespaceForm();
        }
      }
    });

    effect(() => {
      const configItems = this.configItemsForApplication();
      const selected = this.selectedConfigItemId();
      if (!configItems.some((configItem) => configItem.configItemId === selected)) {
        const nextConfigItem = configItems[0] ?? null;
        this.selectedConfigItemId.set(nextConfigItem?.configItemId ?? null);
        if (nextConfigItem) {
          this.loadConfigItem(nextConfigItem);
        } else {
          this.resetConfigItemForm();
        }
      }
    });
  }

  ngOnInit(): void {
    void this.store.initialize();
  }

  protected selectApplication(application: ApplicationSummaryResponse): void {
    this.selectedApplicationId.set(application.applicationId);
    this.loadApplication(application);
  }

  protected selectNamespace(catalogNamespace: NamespaceSummaryResponse): void {
    this.selectedNamespaceId.set(catalogNamespace.namespaceId);
    this.loadNamespace(catalogNamespace);
  }

  protected selectConfigItem(configItem: ConfigItemSummaryResponse): void {
    this.selectedConfigItemId.set(configItem.configItemId);
    this.loadConfigItem(configItem);
  }

  protected resetApplicationForm(): void {
    this.applicationEditingId = null;
    this.applicationForm = this.createApplicationDraft();
  }

  protected resetNamespaceForm(): void {
    this.namespaceEditingId = null;
    this.namespaceForm = this.createNamespaceDraft();
  }

  protected resetConfigItemForm(): void {
    this.configItemEditingId = null;
    this.configItemForm = this.createConfigItemDraft();
  }

  protected async saveApplication(): Promise<void> {
    const request = { ...this.applicationForm };
    const expectedName = request.name;
    if (this.applicationEditingId) {
      await this.store.updateApplication(this.applicationEditingId, request as UpdateApplicationRequest);
    } else {
      await this.store.createApplication(request);
    }

    const created = this.store.applications().find((application) => application.name === expectedName);
    if (created) {
      this.selectApplication(created);
    }
  }

  protected async deleteApplication(): Promise<void> {
    if (!this.applicationEditingId) {
      return;
    }

    await this.store.deleteApplication(this.applicationEditingId);
    this.resetApplicationForm();
  }

  protected async saveNamespace(): Promise<void> {
    const applicationId = this.selectedApplicationId();
    if (!applicationId) {
      return;
    }

    const request = {
      ...this.namespaceForm,
      applicationId
    };
    const expectedPath = request.path || request.name;
    if (this.namespaceEditingId) {
      await this.store.updateNamespace(this.namespaceEditingId, request as UpdateNamespaceRequest);
    } else {
      await this.store.createNamespace(request);
    }

    const created = this.namespacesForApplication().find(
      (catalogNamespace) => catalogNamespace.path === expectedPath
    );
    if (created) {
      this.selectNamespace(created);
    }
  }

  protected async deleteNamespace(): Promise<void> {
    if (!this.namespaceEditingId) {
      return;
    }

    await this.store.deleteNamespace(this.namespaceEditingId);
    this.resetNamespaceForm();
  }

  protected async saveConfigItem(): Promise<void> {
    const namespaceId = this.selectedNamespaceId();
    if (!namespaceId) {
      return;
    }

    const request = {
      ...this.configItemForm,
      namespaceId
    };
    const expectedKey = request.key;
    if (this.configItemEditingId) {
      await this.store.updateConfigItem(this.configItemEditingId, request as UpdateConfigItemRequest);
    } else {
      await this.store.createConfigItem(request);
    }

    const created = this.configItemsForApplication().find((configItem) => configItem.key === expectedKey);
    if (created) {
      this.selectConfigItem(created);
    }
  }

  protected async deleteConfigItem(): Promise<void> {
    if (!this.configItemEditingId) {
      return;
    }

    await this.store.deleteConfigItem(this.configItemEditingId);
    this.resetConfigItemForm();
  }

  protected async createAssignment(): Promise<void> {
    const applicationId = this.selectedApplicationId();
    if (!applicationId || !this.assignmentEnvironmentId || !this.assignmentTargetId) {
      return;
    }

    const request: CreateApplicationAssignmentRequest = {
      applicationId,
      environmentId: this.assignmentEnvironmentId,
      nodeGroupId: this.assignmentTargetMode === 'nodeGroup' ? this.assignmentTargetId : null,
      managedNodeId: this.assignmentTargetMode === 'managedNode' ? this.assignmentTargetId : null,
      enabled: this.assignmentEnabled
    };

    await this.store.createApplicationAssignment(request);
    this.assignmentTargetId = '';
  }

  protected refresh(): void {
    void this.store.refreshAll();
  }

  protected availableAssignmentTargets() {
    if (!this.assignmentEnvironmentId) {
      return [];
    }

    return this.assignmentTargetMode === 'nodeGroup'
      ? this.store.nodeGroups().filter((nodeGroup) => nodeGroup.environmentId === this.assignmentEnvironmentId)
      : this.store.nodes().filter((node) => node.environmentId === this.assignmentEnvironmentId);
  }

  protected assignmentTargetKey(
    target: NodeGroupSummaryResponse | ManagedNodeSummaryResponse
  ): string {
    if (this.assignmentTargetMode === 'nodeGroup') {
      return (target as NodeGroupSummaryResponse).nodeGroupId;
    }

    return (target as ManagedNodeSummaryResponse).nodeId;
  }

  protected resolveApplicationName(applicationId: string): string {
    return (
      this.store.applications().find((application) => application.applicationId === applicationId)?.name ??
      'Unknown application'
    );
  }

  protected resolveEnvironmentName(environmentId: string): string {
    return (
      this.store.environments().find((environment) => environment.environmentId === environmentId)?.name ??
      'Unknown environment'
    );
  }

  protected resolveNodeGroupName(nodeGroupId: string | null): string {
    if (!nodeGroupId) {
      return 'Direct assignment';
    }

    return (
      this.store.nodeGroups().find((nodeGroup) => nodeGroup.nodeGroupId === nodeGroupId)?.name ??
      'Unknown node group'
    );
  }

  protected resolveNodeName(nodeId: string | null): string {
    if (!nodeId) {
      return 'Not bound';
    }

    return this.store.nodes().find((node) => node.nodeId === nodeId)?.name ?? 'Unknown node';
  }

  private loadApplication(application: ApplicationSummaryResponse): void {
    this.applicationEditingId = application.applicationId;
    this.applicationForm = {
      name: application.name,
      slug: application.slug,
      description: application.description,
      defaultIntegrationMode: application.defaultIntegrationMode
    };
  }

  private loadNamespace(catalogNamespace: NamespaceSummaryResponse): void {
    this.namespaceEditingId = catalogNamespace.namespaceId;
    this.namespaceForm = {
      applicationId: catalogNamespace.applicationId,
      name: catalogNamespace.name,
      path: catalogNamespace.path,
      description: catalogNamespace.description
    };
  }

  private loadConfigItem(configItem: ConfigItemSummaryResponse): void {
    this.configItemEditingId = configItem.configItemId;
    this.configItemForm = {
      namespaceId: configItem.namespaceId,
      key: configItem.key,
      valueType: configItem.valueType,
      isSecret: configItem.isSecret,
      isRequired: configItem.isRequired,
      defaultRolloutPolicy: configItem.defaultRolloutPolicy,
      validationSchemaJson: configItem.validationSchemaJson,
      description: configItem.description
    };
  }

  private createApplicationDraft(): CreateApplicationRequest {
    return {
      name: '',
      slug: '',
      description: '',
      defaultIntegrationMode: 'runtime-api'
    };
  }

  private createNamespaceDraft(): CreateNamespaceRequest {
    return {
      applicationId: this.selectedApplicationId() ?? '',
      name: '',
      path: '',
      description: ''
    };
  }

  private createConfigItemDraft(): CreateConfigItemRequest {
    return {
      namespaceId: this.selectedNamespaceId() ?? '',
      key: '',
      valueType: 'string',
      isSecret: false,
      isRequired: false,
      defaultRolloutPolicy: 'immediate',
      validationSchemaJson: '{}',
      description: ''
    };
  }
}