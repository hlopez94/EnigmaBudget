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
});
