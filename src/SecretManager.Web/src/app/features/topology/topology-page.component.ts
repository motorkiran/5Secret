import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TopologyCatalogStore } from '../../core/state/topology-catalog.store';
import {
  CreateEnvironmentRequest,
  CreateManagedNodeRequest,
  CreateNodeGroupRequest,
  EnvironmentSummaryResponse,
  ManagedNodeSummaryResponse,
  NodeGroupSummaryResponse,
  UpdateEnvironmentRequest,
  UpdateManagedNodeRequest,
  UpdateNodeGroupRequest
} from '../../core/models/control-plane.models';

@Component({
  selector: 'app-topology-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './topology-page.component.html',
  styleUrl: './topology-page.component.scss'
})
export class TopologyPageComponent implements OnInit {
  protected readonly store = inject(TopologyCatalogStore);

  protected readonly selectedEnvironmentId = signal<string | null>(null);
  protected readonly selectedNodeGroupId = signal<string | null>(null);
  protected readonly selectedNodeId = signal<string | null>(null);

  protected readonly selectedEnvironment = computed(
    () =>
      this.store
        .environments()
        .find((environment) => environment.environmentId === this.selectedEnvironmentId()) ?? null
  );

  protected readonly nodeGroupsForEnvironment = computed(() => {
    const environmentId = this.selectedEnvironmentId();
    return environmentId
      ? this.store.nodeGroups().filter((nodeGroup) => nodeGroup.environmentId === environmentId)
      : [];
  });

  protected readonly selectedNodeGroup = computed(
    () =>
      this.nodeGroupsForEnvironment().find(
        (nodeGroup) => nodeGroup.nodeGroupId === this.selectedNodeGroupId()
      ) ?? null
  );

  protected readonly nodesForEnvironment = computed(() => {
    const environmentId = this.selectedEnvironmentId();
    return environmentId
      ? this.store.nodes().filter((node) => node.environmentId === environmentId)
      : [];
  });

  protected readonly selectedNode = computed(
    () => this.nodesForEnvironment().find((node) => node.nodeId === this.selectedNodeId()) ?? null
  );

  protected environmentForm: CreateEnvironmentRequest = this.createEnvironmentDraft();
  protected nodeGroupForm: CreateNodeGroupRequest = this.createNodeGroupDraft();
  protected nodeForm: CreateManagedNodeRequest = this.createNodeDraft();

  protected environmentEditingId: string | null = null;
  protected nodeGroupEditingId: string | null = null;
  protected nodeEditingId: string | null = null;

  constructor() {
    effect(() => {
      const environments = this.store.environments();
      const selected = this.selectedEnvironmentId();
      if (!environments.some((environment) => environment.environmentId === selected)) {
        const nextId = environments[0]?.environmentId ?? null;
        this.selectedEnvironmentId.set(nextId);
        const nextEnvironment = environments[0] ?? null;
        if (nextEnvironment) {
          this.loadEnvironment(nextEnvironment);
        } else {
          this.resetEnvironmentForm();
        }
      }
    });

    effect(() => {
      const nodeGroups = this.nodeGroupsForEnvironment();
      const selected = this.selectedNodeGroupId();
      if (!nodeGroups.some((nodeGroup) => nodeGroup.nodeGroupId === selected)) {
        const nextNodeGroup = nodeGroups[0] ?? null;
        this.selectedNodeGroupId.set(nextNodeGroup?.nodeGroupId ?? null);
        if (nextNodeGroup) {
          this.loadNodeGroup(nextNodeGroup);
        } else {
          this.resetNodeGroupForm();
        }
      }
    });

    effect(() => {
      const nodes = this.nodesForEnvironment();
      const selected = this.selectedNodeId();
      if (!nodes.some((node) => node.nodeId === selected)) {
        const nextNode = nodes[0] ?? null;
        this.selectedNodeId.set(nextNode?.nodeId ?? null);
        if (nextNode) {
          this.loadNode(nextNode);
        } else {
          this.resetNodeForm();
        }
      }
    });
  }

  ngOnInit(): void {
    void this.store.initialize();
  }

  protected selectEnvironment(environment: EnvironmentSummaryResponse): void {
    this.selectedEnvironmentId.set(environment.environmentId);
    this.loadEnvironment(environment);
  }

  protected selectNodeGroup(nodeGroup: NodeGroupSummaryResponse): void {
    this.selectedNodeGroupId.set(nodeGroup.nodeGroupId);
    this.loadNodeGroup(nodeGroup);
  }

  protected selectNode(node: ManagedNodeSummaryResponse): void {
    this.selectedNodeId.set(node.nodeId);
    this.loadNode(node);
  }

  protected resetEnvironmentForm(): void {
    this.environmentEditingId = null;
    this.environmentForm = this.createEnvironmentDraft();
  }

  protected resetNodeGroupForm(): void {
    this.nodeGroupEditingId = null;
    this.nodeGroupForm = this.createNodeGroupDraft();
  }

  protected resetNodeForm(): void {
    this.nodeEditingId = null;
    this.nodeForm = this.createNodeDraft();
  }

  protected async saveEnvironment(): Promise<void> {
    const request: CreateEnvironmentRequest = { ...this.environmentForm };
    const expectedName = request.name;
    if (this.environmentEditingId) {
      await this.store.updateEnvironment(this.environmentEditingId, request as UpdateEnvironmentRequest);
    } else {
      await this.store.createEnvironment(request);
    }

    const created = this.store.environments().find((environment) => environment.name === expectedName);
    if (created) {
      this.selectEnvironment(created);
    }
  }

  protected async deleteEnvironment(): Promise<void> {
    if (!this.environmentEditingId) {
      return;
    }

    await this.store.deleteEnvironment(this.environmentEditingId);
    this.resetEnvironmentForm();
  }

  protected async saveNodeGroup(): Promise<void> {
    const environmentId = this.selectedEnvironmentId();
    if (!environmentId) {
      return;
    }

    const request: CreateNodeGroupRequest = {
      ...this.nodeGroupForm,
      environmentId
    };
    const expectedName = request.name;
    if (this.nodeGroupEditingId) {
      await this.store.updateNodeGroup(this.nodeGroupEditingId, request as UpdateNodeGroupRequest);
    } else {
      await this.store.createNodeGroup(request);
    }

    const created = this.nodeGroupsForEnvironment().find((nodeGroup) => nodeGroup.name === expectedName);
    if (created) {
      this.selectNodeGroup(created);
    }
  }

  protected async deleteNodeGroup(): Promise<void> {
    if (!this.nodeGroupEditingId) {
      return;
    }

    await this.store.deleteNodeGroup(this.nodeGroupEditingId);
    this.resetNodeGroupForm();
  }

  protected async saveNode(): Promise<void> {
    const environmentId = this.selectedEnvironmentId();
    if (!environmentId) {
      return;
    }

    const request: CreateManagedNodeRequest = {
      ...this.nodeForm,
      environmentId,
      nodeGroupId: this.nodeForm.nodeGroupId || null,
      lastSeenAtUtc: this.nodeForm.lastSeenAtUtc || null
    };
    const expectedName = request.name;
    if (this.nodeEditingId) {
      await this.store.updateNode(this.nodeEditingId, request as UpdateManagedNodeRequest);
    } else {
      await this.store.createNode(request);
    }

    const created = this.nodesForEnvironment().find((node) => node.name === expectedName);
    if (created) {
      this.selectNode(created);
    }
  }

  protected async deleteNode(): Promise<void> {
    if (!this.nodeEditingId) {
      return;
    }

    await this.store.deleteNode(this.nodeEditingId);
    this.resetNodeForm();
  }

  protected resolveNodeGroupName(nodeGroupId: string | null): string {
    if (!nodeGroupId) {
      return 'Direct environment assignment';
    }

    return (
      this.store.nodeGroups().find((nodeGroup) => nodeGroup.nodeGroupId === nodeGroupId)?.name ?? 'Unknown group'
    );
  }

  protected refresh(): void {
    void this.store.refreshAll();
  }

  private loadEnvironment(environment: EnvironmentSummaryResponse): void {
    this.environmentEditingId = environment.environmentId;
    this.environmentForm = {
      name: environment.name,
      slug: environment.slug,
      description: environment.description,
      isProtected: environment.isProtected
    };
  }

  private loadNodeGroup(nodeGroup: NodeGroupSummaryResponse): void {
    this.nodeGroupEditingId = nodeGroup.nodeGroupId;
    this.nodeGroupForm = {
      environmentId: nodeGroup.environmentId,
      name: nodeGroup.name,
      slug: nodeGroup.slug,
      description: nodeGroup.description
    };
  }

  private loadNode(node: ManagedNodeSummaryResponse): void {
    this.nodeEditingId = node.nodeId;
    this.nodeForm = {
      environmentId: node.environmentId,
      nodeGroupId: node.nodeGroupId,
      name: node.name,
      hostname: node.hostname,
      platform: node.platform,
      status: node.status,
      lastSeenAtUtc: node.lastSeenAtUtc,
      agentVersion: node.agentVersion,
      rolloutPolicyDefault: node.rolloutPolicyDefault
    };
  }

  private createEnvironmentDraft(): CreateEnvironmentRequest {
    return {
      name: '',
      slug: '',
      description: '',
      isProtected: false
    };
  }

  private createNodeGroupDraft(): CreateNodeGroupRequest {
    return {
      environmentId: this.selectedEnvironmentId() ?? '',
      name: '',
      slug: '',
      description: ''
    };
  }

  private createNodeDraft(): CreateManagedNodeRequest {
    return {
      environmentId: this.selectedEnvironmentId() ?? '',
      nodeGroupId: null,
      name: '',
      hostname: '',
      platform: 'linux',
      status: 'Healthy',
      lastSeenAtUtc: null,
      agentVersion: '',
      rolloutPolicyDefault: 'immediate'
    };
  }
}