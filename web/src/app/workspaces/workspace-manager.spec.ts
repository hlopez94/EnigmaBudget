import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { WorkspaceManager } from './workspace-manager';
import { ApiService } from '../core/api.service';
import { Workspace } from '../core/api.models';

const readerPersonal: Workspace = {
  id: 'shared-personal', name: 'Personal', ownerDisplayName: 'Ana López', kind: 'Personal', role: 'Reader',
  isSharedPersonalRead: true, isArchived: false, version: 1,
  permissions: { read: true, readGroups: false, recordFinance: false, manageResources: false, correctHistory: false, manageMembers: false, managePrivacy: false },
};

describe('workspace management permissions', () => {
  it('labels a granted Personal by owner without fetching management-only member details', async () => {
    const api = {
      workspace: vi.fn().mockResolvedValue(readerPersonal), members: vi.fn(), invitations: vi.fn(), personalSharing: vi.fn(),
    };
    const manager = new WorkspaceManager(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    manager.activeWorkspace = readerPersonal;
    manager.open = true;
    await manager.ngOnChanges({ open: { currentValue: true } as never });

    expect(manager.label(readerPersonal)).toBe('Personal de Ana López · Solo lectura');
    expect(api.members).not.toHaveBeenCalled();
    expect(api.invitations).not.toHaveBeenCalled();
    expect(api.personalSharing).not.toHaveBeenCalled();
  });

  it('does not request household members or invitations for a Reader', async () => {
    const householdReader = { ...readerPersonal, id: 'household', name: 'Casa', kind: 'Household' as const };
    const api = { workspace: vi.fn().mockResolvedValue(householdReader), members: vi.fn(), invitations: vi.fn(), personalSharing: vi.fn() };
    const manager = new WorkspaceManager(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    manager.activeWorkspace = householdReader;
    manager.open = true;
    await manager.ngOnChanges({ open: { currentValue: true } as never });

    expect(api.members).not.toHaveBeenCalled();
    expect(api.invitations).not.toHaveBeenCalled();
  });

  it('leaves with the authorized membership version instead of needing the member roster', async () => {
    const workspace = {
      ...readerPersonal,
      id: 'household', name: 'Casa', kind: 'Household' as const, role: 'Member' as const,
      membershipVersion: 3, permissions: { ...readerPersonal.permissions, readGroups: true, recordFinance: true },
    };
    const api = { leaveWorkspace: vi.fn().mockResolvedValue(undefined), workspace: vi.fn().mockResolvedValue(workspace) };
    const manager = new WorkspaceManager(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    manager.activeWorkspace = workspace;
    vi.stubGlobal('confirm', () => true);

    await manager.leave();

    expect(api.leaveWorkspace).toHaveBeenCalledWith('household', 3);
    vi.unstubAllGlobals();
  });

  it('keeps the invite request ID when the same request must be retried after a failed response', async () => {
    const household = {
      ...readerPersonal, id: 'household', name: 'Casa', kind: 'Household' as const, role: 'Owner' as const,
      permissions: { ...readerPersonal.permissions, manageMembers: true },
    };
    const invitation = { id: 'invitation', email: 'member@example.test', role: 'Member' as const, status: 'Pending' as const, expiresAt: '2026-10-30T00:00:00Z', version: 1, shareUrl: '/#invite=token' };
    const api = { createInvitation: vi.fn().mockRejectedValueOnce(new Error('network result unknown')).mockResolvedValueOnce(invitation) };
    const manager = new WorkspaceManager(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    manager.activeWorkspace = household;
    manager.inviteEmail = 'member@example.test';

    await manager.invite();
    await manager.invite();

    expect(api.createInvitation).toHaveBeenCalledTimes(2);
    expect(api.createInvitation.mock.calls[0][1].id).toBe(api.createInvitation.mock.calls[1][1].id);
    expect(manager.shareUrl).toBe('/#invite=token');
  });

  it('treats management inputs as drafts and clears them on confirmed context invalidation', () => {
    const manager = new WorkspaceManager({} as ApiService, { markForCheck: vi.fn() } as never);
    manager.activeWorkspace = { ...readerPersonal, name: 'Personal propio', role: 'Owner' };
    manager.editName = 'Nombre nuevo';
    expect(manager.hasPendingDraft()).toBe(true);

    manager.invalidateContext();

    expect(manager.hasPendingDraft()).toBe(false);
    expect(manager.editName).toBe('Personal propio');
  });

  it('preserves drafts when Spaces closes without a confirmed discard', () => {
    const manager = new WorkspaceManager({} as ApiService, { markForCheck: vi.fn() } as never);
    manager.activeWorkspace = { ...readerPersonal, name: 'Personal propio', role: 'Owner' };
    manager.open = true;
    manager.householdName = 'Borrador de Hogar';
    manager.inviteEmail = 'person@example.test';
    manager.editName = 'Nombre nuevo';
    manager.open = false;

    manager.ngOnChanges({ open: { previousValue: true, currentValue: false } as never });

    expect(manager.hasPendingDraft()).toBe(true);
    expect(manager.householdName).toBe('Borrador de Hogar');
    expect(manager.inviteEmail).toBe('person@example.test');
    expect(manager.editName).toBe('Nombre nuevo');
  });

  it('does not overwrite a Household name changed by another administrator', async () => {
    const household = {
      ...readerPersonal, id: 'household', name: 'Casa original', kind: 'Household' as const, role: 'Owner' as const,
      permissions: { ...readerPersonal.permissions, manageResources: true },
    };
    const latest = { ...household, name: 'Nombre de otra administradora', version: 2 };
    const api = { workspace: vi.fn().mockResolvedValue(latest), updateWorkspace: vi.fn().mockResolvedValue(undefined) };
    const manager = new WorkspaceManager(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    manager.activeWorkspace = household;
    manager.invalidateContext();
    manager.editName = 'Nombre obsoleto';

    await manager.saveWorkspace();

    expect(api.updateWorkspace).not.toHaveBeenCalled();
    expect(manager.error).toContain('cambió');
  });

  it('does not undo an archive change made after the owner confirmation opened', async () => {
    const household = {
      ...readerPersonal, id: 'household', name: 'Casa original', kind: 'Household' as const, role: 'Owner' as const,
      isArchived: false, permissions: { ...readerPersonal.permissions, manageResources: true },
    };
    const latest = { ...household, isArchived: true, version: 2 };
    const api = { workspace: vi.fn().mockResolvedValue(latest), updateWorkspace: vi.fn().mockResolvedValue(undefined) };
    const manager = new WorkspaceManager(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    manager.activeWorkspace = household;
    manager.invalidateContext();
    vi.stubGlobal('confirm', () => true);

    await manager.toggleArchive();

    expect(api.updateWorkspace).not.toHaveBeenCalled();
    expect(manager.error).toContain('cambió');
    vi.unstubAllGlobals();
  });
});
