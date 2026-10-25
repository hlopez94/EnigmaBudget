import { ChangeDetectorRef, Component, HostListener, OnDestroy, OnInit, Optional } from '@angular/core';
import { OnboardingState, SessionInfo, Workspace } from './core/api.models';
import { ApiService } from './core/api.service';
import { WorkspaceContext } from './core/workspace-context';
import { OnboardingComponent } from './onboarding/onboarding';
import { SignInComponent } from './onboarding/sign-in';
import { ButtonModule } from '@openng/optimus-ui/button';
import { MessageModule } from '@openng/optimus-ui/message';
import { FinanceShell } from './finance/finance-shell';
import { NativeAuthCoordinator } from './core/native-auth-coordinator';
import { AuthEpoch } from './core/auth-epoch';
import { NativeInvitationLinks } from './native/native-invitation-links';
import { App as CapacitorApp } from '@capacitor/app';
import type { PluginListenerHandle } from '@capacitor/core';
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [ButtonModule, MessageModule, OnboardingComponent, SignInComponent, FinanceShell],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit, OnDestroy {
  private readonly workspaceContext: WorkspaceContext;
  private readonly nativeLinks = new NativeInvitationLinks();
  private loadGeneration = 0;
  private nativeLinksStarted = false;
  private foregroundListener: PluginListenerHandle | null = null;
  constructor(
    private readonly api: ApiService,
    private readonly changeDetector: ChangeDetectorRef,
    @Optional() context?: WorkspaceContext | null,
    @Optional() private readonly nativeAuth?: NativeAuthCoordinator,
    @Optional() private readonly authEpoch?: AuthEpoch,
  ) { this.workspaceContext = context ?? new WorkspaceContext(); }
  session: SessionInfo | null = null;
  state: OnboardingState | null = null;
  financeAvailable = false;
  showSetup = false;
  workspaces: Workspace[] = [];
  activeWorkspace: Workspace | null = null;
  currentUserId = '';
  personalWorkspaceId = '';
  pendingInviteToken: string | null = null;
  busy = true;
  error = '';
  notice = '';
  async ngOnInit(): Promise<void> {
    this.nativeAuth?.setIdentityLostHandler(() => {
      ++this.loadGeneration;
      this.clearPrivateUi();
      this.session = null;
      this.busy = true;
      void this.load();
    });
    this.nativeAuth?.setPermissionLostHandler(() => {
      ++this.loadGeneration;
      this.clearPrivateUi();
      this.session = null;
      this.busy = true;
      void this.load();
    });
    this.pendingInviteToken = this.api.capturePendingInvitation();
    try { await this.startNativeInvitations(); } catch { this.nativeLinksStarted = false; }
    if (this.api.isNativeAndroid) {
      try {
        this.foregroundListener = await CapacitorApp.addListener('appStateChange', ({ isActive }) => {
          if (isActive) void this.load();
        });
      } catch { this.foregroundListener = null; }
    }
    await this.load();
  }
  async ngOnDestroy(): Promise<void> {
    this.nativeAuth?.setIdentityLostHandler(null);
    this.nativeAuth?.setPermissionLostHandler(null);
    await this.foregroundListener?.remove();
    this.foregroundListener = null;
    await this.nativeLinks.stop();
  }
  @HostListener('window:hashchange')
  captureInviteLink(): void {
    const token = this.api.capturePendingInvitation();
    if (token !== this.pendingInviteToken) {
      this.pendingInviteToken = token;
      this.changeDetector.markForCheck();
    }
  }
  async load(): Promise<void> {
    const generation = ++this.loadGeneration;
    let expectedEpoch = this.nativeAuth?.isNative ? this.authEpoch?.snapshot() ?? null : null;
    const current = () => generation === this.loadGeneration
      && (!expectedEpoch || this.matchesEpoch(expectedEpoch));
    this.busy = true;
    this.error = '';
    try {
      if (this.nativeAuth?.isNative && this.currentUserId) {
        const previousUserId = this.currentUserId;
        if (!(await this.nativeAuth.prepareForegroundRestore(previousUserId))) return;
        if (generation !== this.loadGeneration) return;
        expectedEpoch = this.authEpoch?.snapshot() ?? null;
        this.clearPrivateUi(true);
      }
      const session = await this.api.session();
      if (generation !== this.loadGeneration) return;
      expectedEpoch = this.nativeAuth?.isNative ? this.authEpoch?.snapshot() ?? null : null;
      if (!current()) return;
      const incomingUserId = this.nativeAuth?.isNative ? (this.authEpoch?.snapshot().userId ?? null) : null;
      if (this.currentUserId && incomingUserId && this.currentUserId !== incomingUserId) this.clearPrivateUi();
      this.session = session;
      this.nativeLinks.setAuthenticated(session.authenticated);
      if (session.authenticated) {
        const me = await this.api.me();
        if (!current() || (expectedEpoch && (expectedEpoch.userId !== me.id || expectedEpoch.workspaceId !== null))) return;
        if (this.currentUserId && this.currentUserId !== me.id) this.clearPrivateUi();
        this.currentUserId = me.id;
        this.personalWorkspaceId = me.personalWorkspaceId;
        const workspaces = await this.api.workspaces();
        if (!current() || (expectedEpoch && expectedEpoch.userId !== me.id)) return;
        this.workspaces = workspaces;
        const selectedId = this.workspaceContext.restore(me.id, workspaces.map((space) => space.id), me.personalWorkspaceId, false);
        if (this.nativeAuth?.isNative && !(await this.nativeAuth.activateWorkspace(me.id, selectedId || null))) return;
        if (generation !== this.loadGeneration) return;
        expectedEpoch = this.nativeAuth?.isNative ? this.authEpoch?.snapshot() ?? null : null;
        if (expectedEpoch && (expectedEpoch.userId !== me.id || expectedEpoch.workspaceId !== (selectedId || null))) return;
        this.workspaceContext.restore(me.id, workspaces.map((space) => space.id), me.personalWorkspaceId);
        this.activeWorkspace = this.workspaces.find((space) => space.id === selectedId) ?? null;
        this.api.selectWorkspace(selectedId || null);
        const state = await this.api.loadOnboarding();
        if (!current() || (expectedEpoch && (expectedEpoch.userId !== me.id || expectedEpoch.workspaceId !== (selectedId || null)))) return;
        this.state = state;
        this.financeAvailable = true;
      } else {
        this.clearPrivateUi(true);
        this.session = session;
      }
    } catch (error) {
      if (!current()) return;
      if (this.nativeAuth?.isNative && this.currentUserId) {
        this.clearPrivateUi();
        this.session = null;
      }
      this.error = error instanceof Error ? error.message : 'No pudimos cargar tu espacio.';
    } finally {
      if (generation === this.loadGeneration) {
        this.busy = false;
        this.changeDetector.markForCheck();
      }
    }
  }
  async signedIn(): Promise<void> {
    await this.load();
  }
  clearPrivateUiFromSignIn(): void {
    ++this.loadGeneration;
    this.clearPrivateUi(!this.session?.authenticated);
    this.session = null;
    this.busy = true;
    this.error = '';
    this.changeDetector.markForCheck();
  }
  async completed(state: OnboardingState): Promise<void> {
    this.state = state;
    this.showSetup = false;
    this.financeAvailable = true;
    this.changeDetector.markForCheck();
  }
  startSetup(): void {
    if (this.activeWorkspace?.id !== this.personalWorkspaceId) return;
    this.showSetup = true;
    this.changeDetector.markForCheck();
  }
  async logout(): Promise<void> {
    const generation = ++this.loadGeneration;
    this.busy = true;
    this.notice = '';
    try {
      let localCleared = false;
      const clearLocally = () => {
        if (generation !== this.loadGeneration) return;
        this.clearPrivateUi();
        this.session = { authenticated: false, displayName: null, googleConfigured: false, developmentLoginEnabled: false, developmentUsers: [], csrfToken: '' };
        this.nativeLinks.setAuthenticated(false);
        this.busy = true;
        localCleared = true;
        this.changeDetector.markForCheck();
      };
      const serverRevoked = await this.api.logout(clearLocally);
      if (generation !== this.loadGeneration) return;
      if (!localCleared) clearLocally();
      this.notice = serverRevoked ? 'La sesión se cerró y el servidor confirmó la revocación.' : 'La sesión se cerró en este dispositivo. No pudimos confirmar la revocación en el servidor.';
      this.busy = false;
      this.changeDetector.markForCheck();
    } catch (error) {
      if (generation !== this.loadGeneration) return;
      this.error = error instanceof Error ? error.message : 'No pudimos cerrar la sesión.';
      this.busy = false;
      this.changeDetector.markForCheck();
    }
  }

  async selectWorkspace(id: string): Promise<void> {
    const generation = ++this.loadGeneration;
    const userId = this.currentUserId;
    const startingEpoch = this.nativeAuth?.isNative ? this.authEpoch?.snapshot() ?? null : null;
    let workspace = this.workspaces.find((space) => space.id === id);
    if (!workspace) {
      await this.reloadWorkspaces();
      if (generation !== this.loadGeneration || this.currentUserId !== userId) return;
      workspace = this.workspaces.find((space) => space.id === id);
    }
    if (!workspace) return;
    if (generation !== this.loadGeneration || this.currentUserId !== userId || (startingEpoch && !this.matchesEpoch(startingEpoch))) return;
    if (this.nativeAuth?.isNative && userId && !(await this.nativeAuth.activateWorkspace(userId, id))) return;
    if (generation !== this.loadGeneration || this.currentUserId !== userId) return;
    if (this.authEpoch && this.nativeAuth?.isNative) {
      const snapshot = this.authEpoch.snapshot();
      if (snapshot.userId !== userId || snapshot.workspaceId !== id) return;
    }
    this.workspaceContext.select(id);
    this.api.selectWorkspace(id);
    this.activeWorkspace = workspace;
    this.changeDetector.markForCheck();
  }

  async reloadWorkspaces(): Promise<void> {
    const generation = this.loadGeneration;
    const userId = this.currentUserId;
    const workspaceId = this.activeWorkspace?.id ?? null;
    const contextGeneration = this.workspaceContext.generation;
    const epoch = this.nativeAuth?.isNative ? this.authEpoch?.snapshot() ?? null : null;
    const current = () => generation === this.loadGeneration
      && userId === this.currentUserId
      && workspaceId === (this.activeWorkspace?.id ?? null)
      && contextGeneration === this.workspaceContext.generation
      && (!epoch || this.matchesEpoch(epoch));
    try {
      const workspaces = await this.api.workspaces();
      if (!current()) return;
      this.workspaces = workspaces;
      const currentId = this.activeWorkspace?.id;
      if (currentId) {
        const refreshed = this.workspaces.find((space) => space.id === currentId);
        if (refreshed) this.activeWorkspace = refreshed;
        else {
          if (this.nativeAuth?.isNative && this.currentUserId) {
            if (!(await this.nativeAuth.activateWorkspace(this.currentUserId, null))) return;
            if (generation !== this.loadGeneration || userId !== this.currentUserId || workspaceId !== (this.activeWorkspace?.id ?? null) || contextGeneration !== this.workspaceContext.generation) return;
          }
          ++this.loadGeneration;
          this.clearPrivateUi();
          this.activeWorkspace = null;
          this.workspaceContext.select(null);
          this.api.clearWorkspace();
        }
      }
    } catch (error) {
      if (!current()) return;
      this.error = error instanceof Error ? error.message : 'No pudimos actualizar los espacios.';
    }
    this.changeDetector.markForCheck();
  }

  updateWorkspace(workspace: Workspace): void {
    this.workspaces = this.workspaces.map((item) => item.id === workspace.id ? workspace : item);
    if (this.activeWorkspace?.id === workspace.id) this.activeWorkspace = workspace;
    this.changeDetector.markForCheck();
  }

  private async startNativeInvitations(): Promise<void> {
    if (!this.api.isNativeAndroid || !this.nativeAuth || this.nativeLinksStarted) return;
    const { host } = await this.nativeAuth.invitationHost();
    if (!host) return;
    this.nativeLinksStarted = true;
    await this.nativeLinks.start(host, (token) => {
      this.api.captureNativeInvitation(token);
      this.pendingInviteToken = token;
      this.changeDetector.markForCheck();
    });
  }

  private matchesEpoch(expected: { epoch: number; userId: string | null; workspaceId: string | null }): boolean {
    const current = this.authEpoch?.snapshot();
    return current?.epoch === expected.epoch && current.userId === expected.userId && current.workspaceId === expected.workspaceId;
  }

  private clearPrivateUi(preserveInvitation = false): void {
    this.nativeLinks.setAuthenticated(false);
    this.workspaceContext.clear();
    this.api.clearWorkspace();
    if (!preserveInvitation) {
      this.api.clearPendingInvitation();
      this.nativeLinks.clearPendingInvitation();
    }
    this.state = null;
    this.activeWorkspace = null;
    this.workspaces = [];
    this.currentUserId = '';
    this.personalWorkspaceId = '';
    if (!preserveInvitation) this.pendingInviteToken = null;
    this.financeAvailable = false;
    this.showSetup = false;
  }
}
