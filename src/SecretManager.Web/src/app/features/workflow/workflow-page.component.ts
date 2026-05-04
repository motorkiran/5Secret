import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ControlPlaneApiService } from '../../core/api/control-plane-api.service';
import {
  ConfigItemSummaryResponse,
  CreateDraftValueRequest,
  DraftValueResponse,
  EffectivePreviewResponse,
  PublishedVersionDiffResponse,
  PublishedVersionResponse,
  UpdateDraftValueRequest
} from '../../core/models/control-plane.models';
import { TopologyCatalogStore } from '../../core/state/topology-catalog.store';

type DraftScopeType = 'Application' | 'Environment' | 'NodeGroup' | 'ManagedNode' | 'EmergencyOverride';

interface DraftScopeOption {
  id: string;
  label: string;
}

interface DraftEditorForm {
  scopeType: DraftScopeType;
  scopeId: string;
  valueJson: string;
  changeNote: string;
}

interface PublishForm {
  changeSummary: string;
  rolloutPolicy: string;
}

const INSTALLATION_SCOPE_ID = '00000000-0000-0000-0000-000000000001';

@Component({
  selector: 'app-workflow-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './workflow-page.component.html',
  styleUrl: './workflow-page.component.scss'
})
export class WorkflowPageComponent implements OnInit {
  private readonly api = inject(ControlPlaneApiService);
  protected readonly topologyStore = inject(TopologyCatalogStore);

  protected readonly drafts = signal<DraftValueResponse[]>([]);
  protected readonly preview = signal<EffectivePreviewResponse | null>(null);
  protected readonly publishedVersions = signal<PublishedVersionResponse[]>([]);
  protected readonly selectedDiff = signal<PublishedVersionDiffResponse | null>(null);
  protected readonly activeOperation = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly selectedApplicationId = signal<string | null>(null);
  protected readonly selectedEnvironmentId = signal<string | null>(null);
  protected readonly selectedManagedNodeId = signal<string | null>(null);
  protected readonly selectedNamespaceId = signal<string | null>(null);
  protected readonly selectedConfigItemId = signal<string | null>(null);
  protected readonly selectedDraftValueId = signal<string | null>(null);
  protected readonly selectedVersionId = signal<string | null>(null);

  protected readonly namespacesForApplication = computed(() => {
    const applicationId = this.selectedApplicationId();
    return applicationId
      ? this.topologyStore
          .namespaces()
          .filter((catalogNamespace) => catalogNamespace.applicationId === applicationId)
      : [];
  });

  protected readonly configItemsForApplication = computed(() => {
    const applicationId = this.selectedApplicationId();
    const namespaceId = this.selectedNamespaceId();

    if (!applicationId) {
      return [];
    }

    return this.topologyStore.configItems().filter((configItem) => {
      if (configItem.applicationId !== applicationId) {
        return false;
      }

      return !namespaceId || configItem.namespaceId === namespaceId;
    });
  });

  protected readonly nodesForEnvironment = computed(() => {
    const environmentId = this.selectedEnvironmentId();
    return environmentId
      ? this.topologyStore.nodes().filter((node) => node.environmentId === environmentId)
      : [];
  });

  protected readonly nodeGroupsForEnvironment = computed(() => {
    const environmentId = this.selectedEnvironmentId();
    return environmentId
      ? this.topologyStore.nodeGroups().filter((nodeGroup) => nodeGroup.environmentId === environmentId)
      : [];
  });

  protected readonly selectedConfigItem = computed(
    () =>
      this.topologyStore
        .configItems()
        .find((configItem) => configItem.configItemId === this.selectedConfigItemId()) ?? null
  );

  protected readonly draftsForSelectedConfigItem = computed(() => {
    const configItemId = this.selectedConfigItemId();
    return configItemId
      ? this.drafts().filter((draft) => draft.configItemId === configItemId)
      : [];
  });

  protected readonly selectedVersion = computed(
    () =>
      this.publishedVersions().find(
        (version) => version.publishedVersionId === this.selectedVersionId()
      ) ?? null
  );

  protected draftForm: DraftEditorForm = this.createDraftForm();
  protected publishForm: PublishForm = {
    changeSummary: '',
    rolloutPolicy: 'immediate'
  };
  protected rollbackSummary = '';
  protected draftEditingId: string | null = null;
  protected draftValueMasked = false;

  constructor() {
    effect(() => {
      const applications = this.topologyStore.applications();
      const selectedApplicationId = this.selectedApplicationId();
      if (!applications.some((application) => application.applicationId === selectedApplicationId)) {
        this.selectedApplicationId.set(applications[0]?.applicationId ?? null);
      }
    });

    effect(() => {
      const environments = this.topologyStore.environments();
      const selectedEnvironmentId = this.selectedEnvironmentId();
      if (!environments.some((environment) => environment.environmentId === selectedEnvironmentId)) {
        this.selectedEnvironmentId.set(environments[0]?.environmentId ?? null);
      }
    });

    effect(() => {
      const namespaces = this.namespacesForApplication();
      const selectedNamespaceId = this.selectedNamespaceId();
      if (!namespaces.some((catalogNamespace) => catalogNamespace.namespaceId === selectedNamespaceId)) {
        this.selectedNamespaceId.set(namespaces[0]?.namespaceId ?? null);
      }
    });

    effect(() => {
      const nodes = this.nodesForEnvironment();
      const selectedManagedNodeId = this.selectedManagedNodeId();
      if (!nodes.some((node) => node.nodeId === selectedManagedNodeId)) {
        this.selectedManagedNodeId.set(nodes[0]?.nodeId ?? null);
      }
    });

    effect(() => {
      const configItems = this.configItemsForApplication();
      const selectedConfigItemId = this.selectedConfigItemId();
      if (!configItems.some((configItem) => configItem.configItemId === selectedConfigItemId)) {
        this.selectedConfigItemId.set(configItems[0]?.configItemId ?? null);
        this.resetDraftForm();
      }
    });

    effect(() => {
      const drafts = this.draftsForSelectedConfigItem();
      const selectedDraftValueId = this.selectedDraftValueId();
      if (!drafts.some((draft) => draft.draftValueId === selectedDraftValueId)) {
        this.selectedDraftValueId.set(null);
        this.resetDraftForm();
      }
    });

    effect(() => {
      const versions = this.publishedVersions();
      const selectedVersionId = this.selectedVersionId();
      if (!versions.some((version) => version.publishedVersionId === selectedVersionId)) {
        this.selectedVersionId.set(versions[0]?.publishedVersionId ?? null);
        this.selectedDiff.set(null);
      }
    });

    effect(() => {
      const applicationId = this.selectedApplicationId();
      const environmentId = this.selectedEnvironmentId();
      void this.loadContext(applicationId, environmentId);
    });
  }

  ngOnInit(): void {
    void this.initialize();
  }

  protected async initialize(): Promise<void> {
    await this.topologyStore.initialize();
  }

  protected selectNamespace(namespaceId: string | null): void {
    this.selectedNamespaceId.set(namespaceId);
  }

  protected selectConfigItem(configItem: ConfigItemSummaryResponse): void {
    this.selectedConfigItemId.set(configItem.configItemId);
    this.selectedNamespaceId.set(configItem.namespaceId);
    this.resetDraftForm();
  }

  protected selectDraft(draft: DraftValueResponse): void {
    this.selectedDraftValueId.set(draft.draftValueId);
    this.draftEditingId = draft.draftValueId;
    this.draftValueMasked = draft.isValueMasked;
    this.draftForm = {
      scopeType: draft.scopeType as DraftScopeType,
      scopeId: draft.scopeId,
      valueJson: draft.isValueMasked ? '' : draft.valueJson ?? '',
      changeNote: draft.changeNote
    };
  }

  protected selectVersion(versionId: string): void {
    this.selectedVersionId.set(versionId);
    this.selectedDiff.set(null);
  }

  protected resetDraftForm(): void {
    this.selectedDraftValueId.set(null);
    this.draftEditingId = null;
    this.draftValueMasked = false;
    this.draftForm = this.createDraftForm();
    this.syncDraftScopeId(false);
  }

  protected onScopeTypeChanged(scopeType: DraftScopeType): void {
    this.draftForm.scopeType = scopeType;
    this.syncDraftScopeId(false);
  }

  protected refresh(): void {
    void this.refreshAll();
  }

  protected async refreshAll(): Promise<void> {
    await this.run('refresh-workflow', async () => {
      await this.topologyStore.refreshAll();
      await this.loadContext(this.selectedApplicationId(), this.selectedEnvironmentId());
    });
  }

  protected async saveDraft(): Promise<void> {
    const configItemId = this.selectedConfigItemId();
    const applicationId = this.selectedApplicationId();
    if (!configItemId || !applicationId) {
      return;
    }

    this.syncDraftScopeId(true);
    if (!this.draftForm.scopeId) {
      this.errorMessage.set('Select a valid scope target before saving a draft value.');
      return;
    }

    await this.run(this.draftEditingId ? 'update-draft' : 'create-draft', async () => {
      if (this.draftEditingId) {
        const request: UpdateDraftValueRequest = {
          valueJson: this.draftForm.valueJson,
          changeNote: this.draftForm.changeNote
        };
        await this.api.updateDraftValue(this.draftEditingId, request);
      } else {
        const request: CreateDraftValueRequest = {
          configItemId,
          scopeType: this.draftForm.scopeType,
          scopeId: this.draftForm.scopeId,
          valueJson: this.draftForm.valueJson,
          changeNote: this.draftForm.changeNote
        };
        await this.api.createDraftValue(request);
      }

      await this.loadContext(applicationId, this.selectedEnvironmentId());

      const savedDraft = this.drafts().find(
        (draft) =>
          draft.configItemId === configItemId
          && draft.scopeType === this.draftForm.scopeType
          && draft.scopeId === this.draftForm.scopeId
      );

      if (savedDraft) {
        this.selectDraft(savedDraft);
      }
    });
  }

  protected async deleteDraft(): Promise<void> {
    const applicationId = this.selectedApplicationId();
    if (!this.draftEditingId || !applicationId) {
      return;
    }

    await this.run('delete-draft', async () => {
      await this.api.deleteDraftValue(this.draftEditingId!);
      await this.loadContext(applicationId, this.selectedEnvironmentId());
      this.resetDraftForm();
    });
  }

  protected async loadPreview(): Promise<void> {
    const applicationId = this.selectedApplicationId();
    const environmentId = this.selectedEnvironmentId();
    const managedNodeId = this.selectedManagedNodeId();
    if (!applicationId || !environmentId || !managedNodeId) {
      this.errorMessage.set('Select an application, environment, and managed node before loading the effective preview.');
      return;
    }

    await this.run('load-preview', async () => {
      const preview = await this.api.getEffectivePreview({
        applicationId,
        environmentId,
        managedNodeId,
        namespaceId: this.selectedNamespaceId()
      });
      this.preview.set(preview);
    });
  }

  protected async publish(): Promise<void> {
    const applicationId = this.selectedApplicationId();
    const environmentId = this.selectedEnvironmentId();
    if (!applicationId || !environmentId) {
      return;
    }

    await this.run('publish', async () => {
      const response = await this.api.createPublish({
        applicationId,
        environmentId,
        changeSummary: this.publishForm.changeSummary,
        rolloutPolicy: this.publishForm.rolloutPolicy
      });

      await this.loadContext(applicationId, environmentId);
      this.selectedVersionId.set(response.publishedVersion.publishedVersionId);
      this.publishForm.changeSummary = '';
      await this.loadDiff(response.publishedVersion);
    });
  }

  protected async loadSelectedDiff(): Promise<void> {
    const version = this.selectedVersion();
    if (!version) {
      return;
    }

    await this.loadDiff(version);
  }

  protected async rollbackSelectedVersion(): Promise<void> {
    const version = this.selectedVersion();
    const applicationId = this.selectedApplicationId();
    const environmentId = this.selectedEnvironmentId();
    if (!version || !applicationId || !environmentId) {
      return;
    }

    await this.run('rollback', async () => {
      const response = await this.api.rollbackPublishedVersion(version.publishedVersionId, {
        changeSummary: this.rollbackSummary
      });

      await this.loadContext(applicationId, environmentId);
      this.selectedVersionId.set(response.publishedVersion.publishedVersionId);
      this.rollbackSummary = '';

      const rollbackVersion = this.publishedVersions().find(
        (item) => item.publishedVersionId === response.publishedVersion.publishedVersionId
      );
      if (rollbackVersion) {
        await this.loadDiff(rollbackVersion);
      }
    });
  }

  protected availableScopeTargets(): DraftScopeOption[] {
    switch (this.draftForm.scopeType) {
      case 'Application':
        return this.selectedApplicationId()
          ? [{
              id: this.selectedApplicationId()!,
              label: this.resolveApplicationName(this.selectedApplicationId()!)
            }]
          : [];
      case 'Environment':
        return this.selectedEnvironmentId()
          ? [{
              id: this.selectedEnvironmentId()!,
              label: this.resolveEnvironmentName(this.selectedEnvironmentId()!)
            }]
          : [];
      case 'NodeGroup':
        return this.nodeGroupsForEnvironment().map((nodeGroup) => ({
          id: nodeGroup.nodeGroupId,
          label: nodeGroup.name
        }));
      case 'ManagedNode':
        return this.nodesForEnvironment().map((node) => ({
          id: node.nodeId,
          label: `${node.name} · ${node.hostname}`
        }));
      case 'EmergencyOverride':
        return [{
          id: INSTALLATION_SCOPE_ID,
          label: 'Installation emergency override'
        }];
      default:
        return [];
    }
  }

  protected resolveScopeLabel(scopeType: string, scopeId: string): string {
    switch (scopeType) {
      case 'Application':
        return this.resolveApplicationName(scopeId);
      case 'Environment':
        return this.resolveEnvironmentName(scopeId);
      case 'NodeGroup':
        return this.resolveNodeGroupName(scopeId);
      case 'ManagedNode':
        return this.resolveNodeName(scopeId);
      case 'EmergencyOverride':
        return 'Emergency override';
      default:
        return scopeId;
    }
  }

  protected resolveApplicationName(applicationId: string): string {
    return (
      this.topologyStore
        .applications()
        .find((application) => application.applicationId === applicationId)?.name ?? 'Unknown application'
    );
  }

  protected resolveEnvironmentName(environmentId: string): string {
    return (
      this.topologyStore
        .environments()
        .find((environment) => environment.environmentId === environmentId)?.name ?? 'Unknown environment'
    );
  }

  protected resolveNodeGroupName(nodeGroupId: string): string {
    return (
      this.topologyStore
        .nodeGroups()
        .find((nodeGroup) => nodeGroup.nodeGroupId === nodeGroupId)?.name ?? 'Unknown node group'
    );
  }

  protected resolveNodeName(nodeId: string): string {
    return (
      this.topologyStore.nodes().find((node) => node.nodeId === nodeId)?.name ?? 'Unknown managed node'
    );
  }

  protected maskedValueLabel(valueJson: string | null, isMasked: boolean): string {
    if (isMasked) {
      return 'Masked by default';
    }

    return valueJson ?? 'null';
  }

  private async loadContext(applicationId: string | null, environmentId: string | null): Promise<void> {
    if (!applicationId) {
      this.drafts.set([]);
      this.publishedVersions.set([]);
      this.preview.set(null);
      this.selectedDiff.set(null);
      return;
    }

    await this.run('load-context', async () => {
      const [drafts, versions] = await Promise.all([
        this.api.listDraftValues({ applicationId }),
        environmentId
          ? this.api.listPublishedVersions({ applicationId, environmentId })
          : Promise.resolve([])
      ]);

      this.drafts.set(drafts);
      this.publishedVersions.set(versions);
      this.selectedDiff.set(null);
      this.preview.set(null);
    });
  }

  private async loadDiff(version: PublishedVersionResponse): Promise<void> {
    if (!version.supersedesVersionId) {
      this.selectedDiff.set(null);
      return;
    }

    await this.run('load-diff', async () => {
      const diff = await this.api.getPublishedVersionDiff(
        version.publishedVersionId,
        version.supersedesVersionId
      );
      this.selectedDiff.set(diff);
    });
  }

  private syncDraftScopeId(preserveCurrent: boolean): void {
    const options = this.availableScopeTargets();
    if (options.length === 0) {
      this.draftForm.scopeId = '';
      return;
    }

    if (preserveCurrent && options.some((option) => option.id === this.draftForm.scopeId)) {
      return;
    }

    this.draftForm.scopeId = options[0].id;
  }

  private createDraftForm(): DraftEditorForm {
    return {
      scopeType: 'Application',
      scopeId: this.selectedApplicationId() ?? '',
      valueJson: '',
      changeNote: ''
    };
  }

  private async run(operationName: string, action: () => Promise<void>): Promise<void> {
    this.activeOperation.set(operationName);
    this.errorMessage.set(null);

    try {
      await action();
    } catch (error) {
      this.errorMessage.set(this.describeError(error));
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