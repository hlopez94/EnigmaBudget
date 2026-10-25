import { ChangeDetectorRef, Component, OnInit, Optional } from '@angular/core';
import { OnboardingState, SessionInfo, Workspace } from './core/api.models';
import { ApiService } from './core/api.service';
import { WorkspaceContext } from './core/workspace-context';
import { OnboardingComponent } from './onboarding/onboarding';
import { SignInComponent } from './onboarding/sign-in';
import { ButtonModule } from '@openng/optimus-ui/button';
import { MessageModule } from '@openng/optimus-ui/message';
import { FinanceShell } from './finance/finance-shell';
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [ButtonModule, MessageModule, OnboardingComponent, SignInComponent, FinanceShell],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  private readonly workspaceContext: WorkspaceContext;
  constructor(
    private readonly api: ApiService,
    private readonly changeDetector: ChangeDetectorRef,
    @Optional() context?: WorkspaceContext | null,
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
  async ngOnInit(): Promise<void> {
    this.pendingInviteToken = this.api.capturePendingInvitation();
    await this.load();
  }
  async load(): Promise<void> {
    this.busy = true;
    this.error = '';
    try {
      this.session = await this.api.session();
      if (this.session.authenticated) {
        const me = await this.api.me();
        this.currentUserId = me.id;
        this.personalWorkspaceId = me.personalWorkspaceId;
        this.workspaces = await this.api.workspaces();
        const selectedId = this.workspaceContext.restore(me.id, this.workspaces.map((space) => space.id), me.personalWorkspaceId);
        this.activeWorkspace = this.workspaces.find((space) => space.id === selectedId) ?? null;
        this.api.selectWorkspace(selectedId || null);
        this.state = await this.api.loadOnboarding();
        this.financeAvailable = true;
      } else {
        this.activeWorkspace = null;
        this.workspaces = [];
        this.api.clearWorkspace();
      }
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'No pudimos cargar tu espacio.';
    } finally {
      this.busy = false;
      this.changeDetector.markForCheck();
    }
  }
  async signedIn(): Promise<void> {
    await this.load();
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
    this.busy = true;
    try {
      await this.api.logout();
      this.workspaceContext.clear();
      this.api.clearWorkspace();
      this.state = null;
      this.activeWorkspace = null;
      this.workspaces = [];
      this.currentUserId = '';
      this.personalWorkspaceId = '';
      this.pendingInviteToken = null;
      this.financeAvailable = false;
      this.showSetup = false;
      await this.load();
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'No pudimos cerrar la sesión.';
      this.busy = false;
      this.changeDetector.markForCheck();
    }
  }

  async selectWorkspace(id: string): Promise<void> {
    let workspace = this.workspaces.find((space) => space.id === id);
    if (!workspace) {
      await this.reloadWorkspaces();
      workspace = this.workspaces.find((space) => space.id === id);
    }
    if (!workspace) return;
    this.workspaceContext.select(id);
    this.api.selectWorkspace(id);
    this.activeWorkspace = workspace;
    this.changeDetector.markForCheck();
  }

  async reloadWorkspaces(): Promise<void> {
    try {
      this.workspaces = await this.api.workspaces();
      const currentId = this.activeWorkspace?.id;
      if (currentId) {
        const refreshed = this.workspaces.find((space) => space.id === currentId);
        if (refreshed) this.activeWorkspace = refreshed;
        else {
          this.activeWorkspace = null;
          this.workspaceContext.select(null);
          this.api.clearWorkspace();
        }
      }
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'No pudimos actualizar los espacios.';
    }
    this.changeDetector.markForCheck();
  }

  updateWorkspace(workspace: Workspace): void {
    this.workspaces = this.workspaces.map((item) => item.id === workspace.id ? workspace : item);
    if (this.activeWorkspace?.id === workspace.id) this.activeWorkspace = workspace;
    this.changeDetector.markForCheck();
  }
}
