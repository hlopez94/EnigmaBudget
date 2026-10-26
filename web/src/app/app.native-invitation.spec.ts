import '@angular/compiler';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const native = vi.hoisted(() => ({
  launchUrl: null as string | null,
  urlCallback: null as null | ((event: { url: string }) => void),
  remove: vi.fn(async () => undefined),
}));

vi.mock('@capacitor/core', () => ({ Capacitor: { isNativePlatform: () => true, getPlatform: () => 'android' } }));
vi.mock('@capacitor/app', () => ({ App: {
  addListener: vi.fn(async (event: string, callback: (event: { url: string } | { isActive: boolean }) => void) => {
    if (event === 'appUrlOpen') native.urlCallback = callback as (event: { url: string }) => void;
    return { remove: native.remove };
  }),
  getLaunchUrl: vi.fn(async () => native.launchUrl ? { url: native.launchUrl } : {}),
} }));

import { App } from './app';
import { AuthEpoch } from './core/auth-epoch';
import { ApiService } from './core/api.service';
import { SessionInfo } from './core/api.models';
import { WorkspaceContext } from './core/workspace-context';

describe('native invitation lifecycle in App', () => {
  beforeEach(() => {
    native.launchUrl = null;
    native.urlCallback = null;
    native.remove.mockClear();
  });

  it('preserves a cold-start invitation through anonymous load and explicit logout clears it before sign-in', async () => {
    let finishAnonymousSession!: (session: SessionInfo) => void;
    const anonymousSession = new Promise<SessionInfo>(resolve => { finishAnonymousSession = resolve; });
    const anonymous: SessionInfo = { authenticated: false, displayName: null, googleConfigured: false, developmentLoginEnabled: true, developmentUsers: [], csrfToken: '' };
    const authenticated: SessionInfo = { authenticated: true, displayName: 'Bob', googleConfigured: false, developmentLoginEnabled: true, developmentUsers: [], csrfToken: '' };
    native.launchUrl = 'https://invites.example.test/invitar#invite=native-prelogin-token';
    const epoch = new AuthEpoch();
    const api = {
      isNativeAndroid: true,
      capturePendingInvitation: vi.fn().mockReturnValue(null),
      captureNativeInvitation: vi.fn(),
      clearPendingInvitation: vi.fn(),
      clearWorkspace: vi.fn(),
      logout: vi.fn(async (clearLocal: () => void) => { clearLocal(); return false; }),
      session: vi.fn().mockReturnValueOnce(anonymousSession).mockResolvedValueOnce(authenticated),
      me: vi.fn().mockResolvedValue({ id: 'native-bob', displayName: 'Bob', email: null, emailVerified: false, personalWorkspaceId: 'personal-bob' }),
      workspaces: vi.fn().mockResolvedValue([{ id: 'personal-bob' }]),
      selectWorkspace: vi.fn(),
      loadOnboarding: vi.fn().mockResolvedValue({ version: 2, completed: true, accounts: [], cards: [] }),
    };
    const nativeAuth = {
      isNative: true,
      invitationHost: vi.fn().mockResolvedValue({ host: 'invites.example.test' }),
      activateWorkspace: vi.fn(async (userId: string, workspaceId: string | null) => { epoch.advance(userId, workspaceId); return true; }),
      setIdentityLostHandler: vi.fn(),
      setPermissionLostHandler: vi.fn(),
    };
    const app = new App(api as unknown as ApiService, { markForCheck: vi.fn() } as never, new WorkspaceContext(), nativeAuth as never, epoch);

    const startup = app.ngOnInit();
    await vi.waitFor(() => expect(api.session).toHaveBeenCalledOnce());
    expect(native.urlCallback).not.toBeNull();
    finishAnonymousSession(anonymous);
    await startup;

    expect(app.session?.authenticated).toBe(false);
    expect(api.captureNativeInvitation).not.toHaveBeenCalled();
    await app.logout();
    expect(app.pendingInviteToken).toBeNull();
    expect(api.clearPendingInvitation).toHaveBeenCalledOnce();

    native.urlCallback?.({ url: 'https://invites.example.test/invitar#invite=native-after-logout-token' });
    expect(api.captureNativeInvitation).not.toHaveBeenCalled();
    app.clearPrivateUiFromSignIn();
    epoch.advance('native-bob', null);
    await app.load();

    expect(app.session?.authenticated).toBe(true);
    expect(api.captureNativeInvitation).toHaveBeenCalledOnce();
    expect(api.captureNativeInvitation).toHaveBeenCalledWith('native-after-logout-token');
    expect(app.pendingInviteToken).toBe('native-after-logout-token');
    await app.ngOnDestroy();
  });
});
