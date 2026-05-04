import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ControlPlaneApiService } from '../api/control-plane-api.service';
import {
  BootstrapInstallationRequest,
  CurrentUserResponse,
  LoginRequest
} from '../models/control-plane.models';

type SessionPhase = 'loading' | 'bootstrap' | 'login' | 'workspace';
type BusyAction = 'bootstrap' | 'login' | 'logout' | null;

interface SessionSnapshot {
  hasLoaded: boolean;
  isInitialized: boolean;
  installationName: string | null;
  phase: SessionPhase;
  currentUser: CurrentUserResponse | null;
  busyAction: BusyAction;
  errorMessage: string | null;
}

@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly api = inject(ControlPlaneApiService);
  private readonly state = signal<SessionSnapshot>({
    hasLoaded: false,
    isInitialized: false,
    installationName: null,
    phase: 'loading',
    currentUser: null,
    busyAction: null,
    errorMessage: null
  });
  private initializationTask: Promise<void> | null = null;

  readonly phase = computed(() => this.state().phase);
  readonly installationName = computed(() => this.state().installationName);
  readonly currentUser = computed(() => this.state().currentUser);
  readonly errorMessage = computed(() => this.state().errorMessage);
  readonly busyAction = computed(() => this.state().busyAction);
  readonly isBusy = computed(() => this.state().busyAction !== null);
  readonly isInitialized = computed(() => this.state().isInitialized);
  readonly isAuthenticated = computed(() => this.state().currentUser?.isAuthenticated ?? false);

  initialize(): Promise<void> {
    if (this.initializationTask) {
      return this.initializationTask;
    }

    this.initializationTask = this.loadInitialState().finally(() => {
      this.initializationTask = null;
    });

    return this.initializationTask;
  }

  routeForCurrentState(): string {
    const snapshot = this.state();
    if (!snapshot.hasLoaded) {
      return '/';
    }

    if (!snapshot.isInitialized) {
      return '/bootstrap';
    }

    return snapshot.currentUser?.isAuthenticated ? '/app' : '/login';
  }

  clearError(): void {
    this.patch({ errorMessage: null });
  }

  async bootstrap(request: BootstrapInstallationRequest): Promise<boolean> {
    this.patch({ busyAction: 'bootstrap', errorMessage: null });

    try {
      const bootstrapResponse = await this.api.bootstrap(request);
      const loginResponse = await this.api.login({
        username: request.ownerUsername,
        password: request.password
      });

      this.patch({
        hasLoaded: true,
        isInitialized: true,
        installationName: bootstrapResponse.installationName,
        phase: 'workspace',
        currentUser: {
          userId: loginResponse.userId,
          username: loginResponse.username,
          displayName: loginResponse.displayName,
          role: loginResponse.role,
          isAuthenticated: true
        },
        busyAction: null,
        errorMessage: null
      });

      return true;
    } catch (error) {
      this.patch({
        hasLoaded: true,
        isInitialized: false,
        phase: 'bootstrap',
        currentUser: null,
        busyAction: null,
        errorMessage: this.describeError(error)
      });

      return false;
    }
  }

  async signIn(request: LoginRequest): Promise<boolean> {
    this.patch({ busyAction: 'login', errorMessage: null });

    try {
      const response = await this.api.login(request);
      this.patch({
        hasLoaded: true,
        isInitialized: true,
        phase: 'workspace',
        currentUser: {
          userId: response.userId,
          username: response.username,
          displayName: response.displayName,
          role: response.role,
          isAuthenticated: true
        },
        busyAction: null,
        errorMessage: null
      });

      return true;
    } catch (error) {
      this.patch({
        hasLoaded: true,
        isInitialized: true,
        phase: 'login',
        currentUser: null,
        busyAction: null,
        errorMessage: this.describeError(error)
      });

      return false;
    }
  }

  async signOut(): Promise<void> {
    this.patch({ busyAction: 'logout', errorMessage: null });

    try {
      await this.api.logout();
    } finally {
      this.patch({
        hasLoaded: true,
        isInitialized: true,
        phase: 'login',
        currentUser: null,
        busyAction: null,
        errorMessage: null
      });
    }
  }

  private async loadInitialState(): Promise<void> {
    this.patch({
      phase: 'loading',
      busyAction: null,
      errorMessage: null
    });

    try {
      const status = await this.api.getBootstrapStatus();
      if (!status.isInitialized) {
        this.patch({
          hasLoaded: true,
          isInitialized: false,
          installationName: status.installationName,
          phase: 'bootstrap',
          currentUser: null,
          busyAction: null,
          errorMessage: null
        });
        return;
      }

      this.patch({
        hasLoaded: true,
        isInitialized: true,
        installationName: status.installationName
      });

      await this.refreshCurrentUser();
    } catch (error) {
      this.patch({
        hasLoaded: true,
        isInitialized: false,
        phase: 'bootstrap',
        currentUser: null,
        busyAction: null,
        errorMessage: this.describeError(error)
      });
    }
  }

  private async refreshCurrentUser(): Promise<void> {
    try {
      const user = await this.api.getCurrentUser();
      this.patch({
        phase: user.isAuthenticated ? 'workspace' : 'login',
        currentUser: user.isAuthenticated ? user : null,
        busyAction: null,
        errorMessage: null
      });
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.patch({
          phase: 'login',
          currentUser: null,
          busyAction: null,
          errorMessage: null
        });
        return;
      }

      this.patch({
        phase: 'login',
        currentUser: null,
        busyAction: null,
        errorMessage: this.describeError(error)
      });
    }
  }

  private patch(patch: Partial<SessionSnapshot>): void {
    this.state.update((snapshot) => ({
      ...snapshot,
      ...patch
    }));
  }

  private describeError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        return 'The supplied credentials were not accepted.';
      }

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