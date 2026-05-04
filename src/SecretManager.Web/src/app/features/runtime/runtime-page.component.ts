import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ControlPlaneApiService } from '../../core/api/control-plane-api.service';
import {
  AgentStatusResponse,
  AuditEventDetailResponse,
  AuditEventSummaryResponse
} from '../../core/models/control-plane.models';
import { TopologyCatalogStore } from '../../core/state/topology-catalog.store';

@Component({
  selector: 'app-runtime-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './runtime-page.component.html',
  styleUrl: './runtime-page.component.scss'
})
export class RuntimePageComponent implements OnInit {
  private readonly api = inject(ControlPlaneApiService);
  protected readonly topologyStore = inject(TopologyCatalogStore);

  protected readonly agents = signal<AgentStatusResponse[]>([]);
  protected readonly auditEvents = signal<AuditEventSummaryResponse[]>([]);
  protected readonly selectedAgentId = signal<string | null>(null);
  protected readonly selectedAuditEventId = signal<string | null>(null);
  protected readonly agentDetail = signal<AgentStatusResponse | null>(null);
  protected readonly auditDetail = signal<AuditEventDetailResponse | null>(null);
  protected readonly activeOperation = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  protected environmentFilterId = '';
  protected auditTake = 100;

  protected readonly selectedAgent = computed(
    () => this.agents().find((agent) => agent.agentId === this.selectedAgentId()) ?? null
  );

  protected readonly selectedAuditSummary = computed(
    () =>
      this.auditEvents().find((auditEvent) => auditEvent.eventId === this.selectedAuditEventId()) ?? null
  );

  ngOnInit(): void {
    void this.initialize();
  }

  protected async initialize(): Promise<void> {
    await this.topologyStore.initialize();
    await this.refresh();
  }

  protected async refresh(): Promise<void> {
    await this.run('refresh-runtime', async () => {
      const [agents, auditEvents] = await Promise.all([
        this.api.listAgents(this.environmentFilterId || null),
        this.api.listAuditEvents(this.auditTake)
      ]);

      this.agents.set(agents);
      this.auditEvents.set(auditEvents);

      const selectedAgentId = this.selectedAgentId();
      if (!agents.some((agent) => agent.agentId === selectedAgentId)) {
        this.selectedAgentId.set(agents[0]?.agentId ?? null);
        this.agentDetail.set(agents[0] ?? null);
      }

      const selectedAuditEventId = this.selectedAuditEventId();
      if (!auditEvents.some((auditEvent) => auditEvent.eventId === selectedAuditEventId)) {
        this.selectedAuditEventId.set(auditEvents[0]?.eventId ?? null);
        this.auditDetail.set(null);
      }
    });
  }

  protected async applyFilters(): Promise<void> {
    await this.refresh();
  }

  protected async selectAgent(agentId: string): Promise<void> {
    this.selectedAgentId.set(agentId);
    await this.run('load-agent', async () => {
      const detail = await this.api.getAgentStatus(agentId);
      this.agentDetail.set(detail);
    });
  }

  protected async selectAuditEvent(eventId: string): Promise<void> {
    this.selectedAuditEventId.set(eventId);
    await this.run('load-audit-event', async () => {
      const detail = await this.api.getAuditEvent(eventId);
      this.auditDetail.set(detail);
    });
  }

  protected resolveEnvironmentName(environmentId: string): string {
    return (
      this.topologyStore
        .environments()
        .find((environment) => environment.environmentId === environmentId)?.name ?? 'Unknown environment'
    );
  }

  protected resolveNodeName(managedNodeId: string): string {
    return (
      this.topologyStore.nodes().find((node) => node.nodeId === managedNodeId)?.name ?? 'Unknown node'
    );
  }

  protected resolveNodeGroupName(nodeGroupId: string | null): string {
    if (!nodeGroupId) {
      return 'Direct environment assignment';
    }

    return (
      this.topologyStore
        .nodeGroups()
        .find((nodeGroup) => nodeGroup.nodeGroupId === nodeGroupId)?.name ?? 'Unknown node group'
    );
  }

  protected describeAgentFreshness(agent: AgentStatusResponse): string {
    if (!agent.lastSeenAtUtc) {
      return 'No heartbeat received yet';
    }

    return `Last seen ${new Date(agent.lastSeenAtUtc).toLocaleString()}`;
  }

  protected formatDetailsJson(detailsJson: string | null): string {
    if (!detailsJson) {
      return 'No structured detail payload was recorded.';
    }

    try {
      return JSON.stringify(JSON.parse(detailsJson), null, 2);
    } catch {
      return detailsJson;
    }
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