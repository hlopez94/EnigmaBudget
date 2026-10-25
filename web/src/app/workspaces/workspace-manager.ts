import { ChangeDetectionStrategy, ChangeDetectorRef, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiProblem, ApiService } from '../core/api.service';
import { InvitationPreview, PersonalSharing, Workspace, WorkspaceInvitation, WorkspaceMember } from '../core/api.models';

@Component({
  selector: 'app-workspace-manager',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './workspace-manager.html',
  styleUrl: './workspace-manager.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspaceManager implements OnChanges {
  @Input() workspaces: Workspace[] = [];
  @Input() activeWorkspace: Workspace | null = null;
  @Input() personalWorkspaceId = '';
  @Input() currentUserId = '';
  @Input() googleConfigured = false;
  @Input() switchBlocked = false;
  @Input() open = false;
  @Input() pendingInviteToken: string | null = null;
  @Output() workspaceSelected = new EventEmitter<string>();
  @Output() workspacesChanged = new EventEmitter<void>();
  @Output() pendingInviteHandled = new EventEmitter<void>();
  @Output() workspaceUpdated = new EventEmitter<Workspace>();

  householdName = '';
  memberRows: WorkspaceMember[] = [];
  invites: WorkspaceInvitation[] = [];
  sharing: PersonalSharing[] = [];
  inviteEmail = '';
  inviteRole: 'Admin' | 'Member' | 'Reader' = 'Member';
  shareUrl: string | null = null;
  invitePreview: InvitationPreview | null = null;
  editName = '';
  busy = false;
  error = '';
  notice = '';
  private generation = 0;

  constructor(private readonly api: ApiService, private readonly cdr: ChangeDetectorRef) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['activeWorkspace']) {
      this.memberRows = [];
      this.invites = [];
      this.sharing = [];
      this.shareUrl = null;
      this.invitePreview = null;
      if (this.activeWorkspace) this.editName = this.activeWorkspace.name;
    }
    if (this.open && (changes['open'] || changes['activeWorkspace'] || changes['pendingInviteToken'])) void this.load();
  }

  get canManageMembers(): boolean { return !!this.activeWorkspace?.permissions.manageMembers && !this.activeWorkspace?.isArchived; }
  get canManagePrivacy(): boolean { return !!this.activeWorkspace?.permissions.managePrivacy; }
  get isOwnPersonal(): boolean { return this.activeWorkspace?.id === this.personalWorkspaceId; }
  get activeMembers(): WorkspaceMember[] { return this.memberRows.filter(member => member.active); }
  get pendingInvitations(): WorkspaceInvitation[] { return this.invites.filter(invitation => invitation.status === 'Pending'); }
  get currentOwner(): boolean { return this.activeWorkspace?.role === 'Owner'; }

  label(workspace: Workspace): string {
    if (workspace.kind === 'Personal') {
      if (workspace.id === this.personalWorkspaceId) return 'Mi espacio Personal';
      return `Personal de ${workspace.ownerDisplayName?.trim() || 'otra persona'} · Solo lectura`;
    }
    return `${workspace.name} · ${workspace.role === 'Reader' ? 'Solo lectura' : this.roleName(workspace.role)}`;
  }

  async select(id: string): Promise<void> {
    if (!id || this.switchBlocked || id === this.activeWorkspace?.id) return;
    this.workspaceSelected.emit(id);
  }
  refreshSpaces(): void { this.workspacesChanged.emit(); }
  reloginWithGoogle(): void { if (this.pendingInviteToken && this.googleConfigured) this.api.signInGoogle(); }

  async createHousehold(): Promise<void> {
    if (this.busy || !this.householdName.trim()) return;
    this.busy = true; this.error = ''; this.notice = ''; this.cdr.markForCheck();
    try {
      const created = await this.api.createWorkspace({ id: crypto.randomUUID(), kind: 'Household', name: this.householdName.trim() });
      this.householdName = '';
      this.workspacesChanged.emit();
      this.workspaceSelected.emit(created.id);
      this.notice = 'Hogar creado. Esto no activa ni confirma una suscripción.';
    } catch (error) { this.showError(error); }
    finally { this.busy = false; this.cdr.markForCheck(); }
  }

  async invite(): Promise<void> {
    const workspace = this.activeWorkspace;
    if (!workspace || !this.canManageMembers || !this.inviteEmail.trim() || this.busy) return;
    this.busy = true; this.error = ''; this.shareUrl = null; this.notice = ''; this.cdr.markForCheck();
    try {
      const invitation = await this.api.createInvitation(workspace.id, { id: crypto.randomUUID(), email: this.inviteEmail.trim(), role: this.inviteRole });
      this.invites = [invitation, ...this.invites.filter(item => item.id !== invitation.id)];
      this.shareUrl = invitation.shareUrl;
      this.inviteEmail = '';
      this.notice = this.shareUrl ? 'Enlace creado. Copialo ahora; si lo perdés, revocá la invitación y creá otra.' : 'La invitación ya existía y el enlace de un solo uso no se puede recuperar. Revocala y creá otra si se perdió.';
    } catch (error) { this.showError(error); }
    finally { this.busy = false; this.cdr.markForCheck(); }
  }

  async copyInvite(): Promise<void> {
    if (!this.shareUrl) return;
    try { await navigator.clipboard.writeText(new URL(this.shareUrl, window.location.origin).toString()); this.notice = 'Enlace copiado. El token se muestra una sola vez.'; }
    catch { this.error = 'No pudimos copiar el enlace. Mantené esta pantalla abierta e intentá de nuevo.'; }
    this.cdr.markForCheck();
  }

  async changeRole(member: WorkspaceMember, role: string): Promise<void> {
    const workspace = this.activeWorkspace;
    if (!workspace || !this.canManageMembers || !member.active || member.role === 'Owner') return;
    await this.mutate(async () => { await this.api.changeMember(workspace.id, member.userId, { version: member.version, role }); });
  }

  async removeMember(member: WorkspaceMember): Promise<void> {
    const workspace = this.activeWorkspace;
    if (!workspace || !this.canManageMembers || member.role === 'Owner' || !member.active) return;
    if (!window.confirm(`¿Quitar a ${member.displayName} de ${workspace.name}? El historial y las obligaciones se conservan.`)) return;
    await this.mutate(async () => { await this.api.removeMember(workspace.id, member.userId, member.version); });
  }

  async transferOwner(member: WorkspaceMember): Promise<void> {
    const workspace = this.activeWorkspace;
    if (!workspace || !this.currentOwner || workspace.kind !== 'Household' || !member.active || member.role === 'Owner') return;
    if (!window.confirm(`¿Transferir la titularidad de ${workspace.name} a ${member.displayName}?`)) return;
    await this.mutate(async () => { await this.api.transferOwner(workspace.id, workspace.version, member.userId); });
  }

  async leave(): Promise<void> {
    const workspace = this.activeWorkspace;
    if (!workspace || workspace.role === 'Owner') return;
    if (!window.confirm(`¿Salir de ${workspace.name}? Tus obligaciones compartidas se conservan.`)) return;
    if (workspace.membershipVersion === null) return;
    await this.mutate(async () => { await this.api.leaveWorkspace(workspace.id, workspace.membershipVersion!); });
  }

  async revoke(invitation: WorkspaceInvitation): Promise<void> {
    const workspace = this.activeWorkspace;
    if (!workspace || !this.canManageMembers || invitation.status !== 'Pending') return;
    await this.mutate(async () => { await this.api.revokeInvitation(workspace.id, invitation.id, invitation.version); });
  }

  async saveWorkspace(): Promise<void> {
    const workspace = this.activeWorkspace;
    if (!workspace?.permissions.manageResources || !this.editName.trim()) return;
    await this.mutate(async () => { await this.api.updateWorkspace(workspace.id, { version: workspace.version, name: this.editName.trim(), isArchived: workspace.isArchived }); });
  }

  async toggleArchive(): Promise<void> {
    const workspace = this.activeWorkspace;
    if (!workspace?.permissions.manageResources) return;
    const verb = workspace.isArchived ? 'restaurar' : 'archivar';
    if (!window.confirm(`¿${verb} ${workspace.name}? ${workspace.isArchived ? 'Volverá a permitir actividad según sus permisos.' : 'El historial se conservará y las finanzas quedarán en solo lectura.'}`)) return;
    await this.mutate(async () => { await this.api.updateWorkspace(workspace.id, { version: workspace.version, name: this.editName.trim() || workspace.name, isArchived: !workspace.isArchived }); });
  }

  async toggleSharing(item: PersonalSharing): Promise<void> {
    if (!this.isOwnPersonal || !this.canManagePrivacy) return;
    await this.mutate(async () => { await this.api.updatePersonalSharing(item.householdId, item.version, !item.enabled); });
  }

  async acceptInvite(): Promise<void> {
    if (!this.pendingInviteToken || !this.invitePreview || this.busy) return;
    const token = this.pendingInviteToken;
    this.busy = true; this.error = ''; this.cdr.markForCheck();
    try {
      const workspace = await this.api.acceptInvitation(token);
      this.api.clearPendingInvitation();
      this.invitePreview = null;
      this.pendingInviteHandled.emit();
      await this.loadSpaces();
      this.workspaceSelected.emit(workspace.id);
      this.notice = `Te sumaste a ${workspace.name}. Elegí ese espacio para continuar.`;
    } catch (error) { this.showError(error); }
    finally { this.busy = false; this.cdr.markForCheck(); }
  }

  private async load(): Promise<void> {
    const generation = ++this.generation;
    this.error = ''; this.notice = '';
    const workspace = this.activeWorkspace;
    try {
      const selectedWorkspace = workspace;
      const currentWorkspace = selectedWorkspace ? await this.api.workspace(selectedWorkspace.id) : null;
      if (generation !== this.generation) return;
      if (currentWorkspace && JSON.stringify(currentWorkspace) !== JSON.stringify(selectedWorkspace)) this.workspaceUpdated.emit(currentWorkspace);
      const tasks: Promise<void>[] = [];
      if (currentWorkspace?.kind === 'Household' && currentWorkspace.permissions.manageMembers && !currentWorkspace.isArchived) {
        tasks.push(this.api.members(currentWorkspace.id).then(value => { if (generation === this.generation) this.memberRows = value; }));
        tasks.push(this.api.invitations(currentWorkspace.id).then(value => { if (generation === this.generation) this.invites = value; }));
      } else { this.memberRows = []; this.invites = []; }
      if (currentWorkspace?.id === this.personalWorkspaceId && currentWorkspace.permissions.managePrivacy)
        tasks.push(this.api.personalSharing().then(value => { if (generation === this.generation) this.sharing = value; }));
      else this.sharing = [];
      if (this.pendingInviteToken) {
        tasks.push(this.api.previewInvitation(this.pendingInviteToken).then(value => { if (generation === this.generation) this.invitePreview = value; }));
      }
      await Promise.all(tasks);
    } catch (error) {
      if (generation === this.generation) {
        this.memberRows = []; this.invites = []; this.sharing = [];
        if (error instanceof ApiProblem && (error.status === 403 || error.status === 404)) this.workspacesChanged.emit();
        this.showError(error);
      }
    }
    if (generation === this.generation) this.cdr.markForCheck();
  }

  private async mutate(action: () => Promise<void>): Promise<void> {
    this.busy = true; this.error = ''; this.notice = ''; this.cdr.markForCheck();
    try {
      await action();
      await this.loadSpaces();
      this.notice = 'Cambios guardados.';
      await this.load();
    } catch (error) { this.showError(error); }
    finally { this.busy = false; this.cdr.markForCheck(); }
  }

  private async loadSpaces(): Promise<void> { this.workspacesChanged.emit(); }
  private showError(error: unknown): void {
    if (this.pendingInviteToken && error instanceof ApiProblem && error.status === 403) this.error = error.message;
    else if (this.pendingInviteToken && error instanceof ApiProblem && error.status === 404) this.error = 'El enlace venció, fue revocado o no corresponde al correo verificado de esta sesión. Revisá la cuenta iniciada o pedí un enlace nuevo.';
    else if (error instanceof ApiProblem && (error.status === 403 || error.status === 404)) this.error = 'El acceso cambió o el espacio ya no está disponible. Actualizá los espacios y elegí uno disponible.';
    else this.error = error instanceof Error ? error.message : 'No pudimos completar la acción.';
  }
  private roleName(role: string): string { return role === 'Owner' ? 'Titular' : role === 'Admin' ? 'Administración' : role === 'Member' ? 'Integrante' : 'Solo lectura'; }
}
