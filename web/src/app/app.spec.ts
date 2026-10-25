import "@angular/compiler";
import { describe, expect, it, vi } from 'vitest';
import { App } from './app';
import { ApiService } from './core/api.service';
import { OnboardingState, SessionInfo } from './core/api.models';
import { WorkspaceContext } from './core/workspace-context';
import { AuthEpoch } from './core/auth-epoch';

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
    let epochSnapshot = { epoch: 3, userId: 'old-user' as string | null, workspaceId: 'old-space' as string | null };
    const nativeAuth = {
      isNative: true,
      prepareForegroundRestore: vi.fn(async () => { epochSnapshot = { epoch: 4, userId: 'new-user', workspaceId: null }; return true; }),
      activateWorkspace: vi.fn(async (userId: string, workspaceId: string | null) => { epochSnapshot = { epoch: 5, userId, workspaceId }; return true; }),
    };
    const epoch = { snapshot: () => epochSnapshot };
    app = new App(api as unknown as ApiService, { markForCheck: vi.fn() } as never, new WorkspaceContext(), nativeAuth as never, epoch as never);
    app.currentUserId = 'old-user';
    app.state = { completed: true } as never;
    await app.load();
    expect(nativeAuth.prepareForegroundRestore).toHaveBeenCalledWith('old-user');
    expect(nativeAuth.activateWorkspace).toHaveBeenCalledWith('new-user', 'new-space');
    expect(app.currentUserId).toBe('new-user');
    expect(app.state).toEqual({ completed: false });
  });

  it('keeps onboarding state cleared when an old load finishes after logout while revocation is pending', async () => {
    let finishOnboarding!: (value: OnboardingState) => void;
    let confirmRevocation!: (confirmed: boolean) => void;
    const onboarding = new Promise<OnboardingState>(resolve => { finishOnboarding = resolve; });
    const revocation = new Promise<boolean>(resolve => { confirmRevocation = resolve; });
    const workspace = { id: 'space-a', name: 'Personal' } as never;
    const api = {
      session: vi.fn().mockResolvedValue({ authenticated: true, displayName: 'Ana', googleConfigured: false, developmentLoginEnabled: false, developmentUsers: [], csrfToken: '' }),
      me: vi.fn().mockResolvedValue({ id: 'user-a', displayName: 'Ana', email: null, emailVerified: false, personalWorkspaceId: 'space-a' }),
      workspaces: vi.fn().mockResolvedValue([workspace]),
      loadOnboarding: vi.fn(() => onboarding),
      logout: vi.fn((clearLocal: () => void) => { clearLocal(); return revocation; }),
      selectWorkspace: vi.fn(), clearWorkspace: vi.fn(), clearPendingInvitation: vi.fn(),
    };
    const app = new App(api as unknown as ApiService, { markForCheck: vi.fn() } as never, new WorkspaceContext());
    const oldLoad = app.load();
    await vi.waitFor(() => expect(api.loadOnboarding).toHaveBeenCalledOnce());

    const logout = app.logout();
    expect(app.session?.authenticated).toBe(false);
    expect(app.currentUserId).toBe('');
    expect(app.workspaces).toEqual([]);
    expect(app.state).toBeNull();
    expect(app.financeAvailable).toBe(false);

    finishOnboarding({ version: 2, completed: true, accounts: [], cards: [] });
    await oldLoad;
    expect(app.state).toBeNull();
    expect(app.financeAvailable).toBe(false);
    expect(app.currentUserId).toBe('');

    confirmRevocation(false);
    await logout;
    expect(app.notice).toMatch(/no pudimos confirmar la revocación/i);
  });

  it('does not republish user A workspaces after identity transitions to user B', async () => {
    const userAWorkspaces: Array<{ id: string }> = [{ id: 'space-a' }];
    let finishUserAList!: (value: Array<{ id: string }>) => void;
    const userAResponse = new Promise<typeof userAWorkspaces>(resolve => { finishUserAList = resolve; });
    const context = new WorkspaceContext();
    context.restore('user-a', ['space-a'], 'space-a');
    const epoch = new AuthEpoch();
    epoch.advance('user-a', 'space-a');
    const api = { workspaces: vi.fn(() => userAResponse), clearWorkspace: vi.fn(), clearPendingInvitation: vi.fn() };
    const nativeAuth = { isNative: true };
    const app = new App(api as unknown as ApiService, { markForCheck: vi.fn() } as never, context, nativeAuth as never, epoch);
    app.session = { authenticated: true, displayName: 'Ana', googleConfigured: false, developmentLoginEnabled: false, developmentUsers: [], csrfToken: '' };
    app.currentUserId = 'user-a';
    app.workspaces = userAWorkspaces;
    app.activeWorkspace = userAWorkspaces[0] as never;

    const oldReload = app.reloadWorkspaces();
    epoch.advance('user-b', null);
    app.clearPrivateUiFromSignIn();
    expect(app.currentUserId).toBe('');
    expect(app.workspaces).toEqual([]);
    finishUserAList([{ id: 'space-a-stale' } as never]);
    await oldReload;

    expect(app.currentUserId).toBe('');
    expect(app.workspaces).toEqual([]);
    expect(app.activeWorkspace).toBeNull();
    expect(app.state).toBeNull();
    expect(epoch.snapshot()).toMatchObject({ userId: 'user-b', workspaceId: null });
  });

  it('does not commit a delayed workspace reload after selecting a different space', async () => {
    let finishReload!: (value: never[]) => void;
    let finishActivation!: () => void;
    const delayedReload = new Promise<never[]>(resolve => { finishReload = resolve; });
    const activation = new Promise<void>(resolve => { finishActivation = resolve; });
    const spaces = [{ id: 'space-a' }, { id: 'space-b' }] as never[];
    const context = new WorkspaceContext();
    context.restore('user-a', ['space-a', 'space-b'], 'space-a');
    const epoch = new AuthEpoch();
    epoch.advance('user-a', 'space-a');
    const api = {
      workspaces: vi.fn(() => delayedReload), selectWorkspace: vi.fn(), clearWorkspace: vi.fn(), clearPendingInvitation: vi.fn(),
    };
    const nativeAuth = {
      isNative: true,
      activateWorkspace: vi.fn(async (_userId: string, workspaceId: string | null) => {
        if (workspaceId === 'space-b') await activation;
        epoch.advance('user-a', workspaceId);
        return true;
      }),
    };
    const app = new App(api as unknown as ApiService, { markForCheck: vi.fn() } as never, context, nativeAuth as never, epoch);
    app.currentUserId = 'user-a';
    app.workspaces = spaces as never;
    app.activeWorkspace = spaces[0] as never;

    const oldReload = app.reloadWorkspaces();
    const selecting = app.selectWorkspace('space-b');
    await vi.waitFor(() => expect(nativeAuth.activateWorkspace).toHaveBeenCalledWith('user-a', 'space-b'));
    finishReload([{ id: 'space-a' } as never]);
    await oldReload;
    finishActivation();
    await selecting;

    expect(app.workspaces.map(space => space.id)).toEqual(['space-a', 'space-b']);
    expect(app.activeWorkspace?.id).toBe('space-b');
    expect(context.workspaceId).toBe('space-b');
    expect(api.selectWorkspace).toHaveBeenLastCalledWith('space-b');
    expect(epoch.snapshot()).toMatchObject({ userId: 'user-a', workspaceId: 'space-b' });
  });
});
