import "@angular/compiler";
import { describe, expect, it, vi } from 'vitest';
import { App } from './app';
import { ApiService } from './core/api.service';
import { OnboardingState, SessionInfo } from './core/api.models';
import { WorkspaceContext } from './core/workspace-context';

describe('application startup', () => {
  it('refreshes the root view after an async session opens the incomplete wizard', async () => {
    const session: SessionInfo = { authenticated:true, displayName:'Usuario', googleConfigured:false, developmentLoginEnabled:false, developmentUsers:[], csrfToken:'token' };
    const state: OnboardingState = { version:2, completed:false, accounts:[], cards:[] };
    const api = {
      session:vi.fn().mockResolvedValue(session),
      me:vi.fn().mockResolvedValue({ id:'user', displayName:'Usuario', email:null, emailVerified:false, personalWorkspaceId:'personal' }),
      workspaces:vi.fn().mockResolvedValue([{ id:'personal', name:'Personal', ownerDisplayName:'Usuario', kind:'Personal', role:'Owner', isSharedPersonalRead:false, isArchived:false, version:1, permissions:{ read:true, readGroups:true, recordFinance:true, manageResources:true, correctHistory:true, manageMembers:true, managePrivacy:true } }]),
      selectWorkspace:vi.fn(),
      loadOnboarding:vi.fn().mockResolvedValue(state),
    };
    const markForCheck = vi.fn();
    const app = new App(api as unknown as ApiService, { markForCheck } as unknown as import('@angular/core').ChangeDetectorRef);

    await app.load();

    expect(app.busy).toBe(false);
    expect(app.session).toEqual(session);
    expect(app.state).toEqual(state);
    expect(markForCheck).toHaveBeenCalled();
  });

  it('clears selected workspace and pending invite memory on logout', async () => {
    const context = new WorkspaceContext();
    context.restore('user', ['personal'], 'personal');
    const api = {
      logout: vi.fn().mockResolvedValue(undefined),
      clearWorkspace: vi.fn(),
      capturePendingInvitation: vi.fn().mockReturnValue(null),
      session: vi.fn().mockResolvedValue({ authenticated: false, displayName: null, googleConfigured: false, developmentLoginEnabled: false, developmentUsers:[], csrfToken: '' }),
    };
    const app = new App(api as unknown as ApiService, { markForCheck: vi.fn() } as never, context);
    app.activeWorkspace = { id: 'personal' } as never;
    app.workspaces = [{ id: 'personal' } as never];
    app.pendingInviteToken = 'ephemeral-token';

    await app.logout();

    expect(context.workspaceId).toBeNull();
    expect(app.activeWorkspace).toBeNull();
    expect(app.workspaces).toEqual([]);
    expect(app.pendingInviteToken).toBeNull();
    expect(api.clearWorkspace).toHaveBeenCalled();
  });

  it('captures a new invitation fragment without requiring a document reload', () => {
    const api = { capturePendingInvitation: vi.fn().mockReturnValue('new-invite-token') };
    const markForCheck = vi.fn();
    const app = new App(api as unknown as ApiService, { markForCheck } as never);

    app.captureInviteLink();

    expect(app.pendingInviteToken).toBe('new-invite-token');
    expect(markForCheck).toHaveBeenCalled();
  });
});
