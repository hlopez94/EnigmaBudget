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
      clearPendingInvitation: vi.fn(),
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

  it('ignores a delayed restore after a newer load has cleared the prior private state', async () => {
    let finishOld!: (value: SessionInfo) => void;
    const oldSession = new Promise<SessionInfo>(resolve => { finishOld = resolve; });
    const api = {
      session: vi.fn().mockReturnValueOnce(oldSession).mockResolvedValueOnce({ authenticated: false, displayName: null, googleConfigured: false, developmentLoginEnabled: false, developmentUsers: [], csrfToken: '' }),
      clearWorkspace: vi.fn(), clearPendingInvitation: vi.fn(),
    };
    const app = new App(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    app.currentUserId = 'prior-user';
    app.state = { completed: true } as never;
    const oldLoad = app.load();
    await app.load();
    finishOld({ authenticated: true, displayName: 'Viejo', googleConfigured: false, developmentLoginEnabled: false, developmentUsers: [], csrfToken: '' });
    await oldLoad;
    expect(app.currentUserId).toBe('');
    expect(app.state).toBeNull();
    expect(app.session?.authenticated).toBe(false);
  });

  it('invalidates the old native workspace before loading an identity change', async () => {
    let app!: App;
    const api = {
      session: vi.fn().mockResolvedValue({ authenticated: true, displayName: 'Nueva', googleConfigured: false, developmentLoginEnabled: false, developmentUsers: [], csrfToken: '' }),
      me: vi.fn(async () => { expect(app.currentUserId).toBe(''); return { id: 'new-user', displayName: 'Nueva', email: null, emailVerified: false, personalWorkspaceId: 'new-space' }; }),
      workspaces: vi.fn().mockResolvedValue([{ id: 'new-space' }]), selectWorkspace: vi.fn(), clearWorkspace: vi.fn(), clearPendingInvitation: vi.fn(),
      loadOnboarding: vi.fn().mockResolvedValue({ completed: false }),
    };
    const nativeAuth = { isNative: true, prepareForegroundRestore: vi.fn().mockResolvedValue(true), activateWorkspace: vi.fn().mockResolvedValue(true) };
    const epoch = { snapshot: () => ({ epoch: 4, userId: 'new-user', workspaceId: null }) };
    app = new App(api as unknown as ApiService, { markForCheck: vi.fn() } as never, new WorkspaceContext(), nativeAuth as never, epoch as never);
    app.currentUserId = 'old-user';
    app.state = { completed: true } as never;
    await app.load();
    expect(nativeAuth.prepareForegroundRestore).toHaveBeenCalledWith('old-user');
    expect(nativeAuth.activateWorkspace).toHaveBeenCalledWith('new-user', 'new-space');
    expect(app.currentUserId).toBe('new-user');
    expect(app.state).toEqual({ completed: false });
  });
});
