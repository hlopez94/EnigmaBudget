import { describe, expect, it, vi } from 'vitest';
import { AuthEpoch } from './auth-epoch';
import { NativeAuthCoordinator } from './native-auth-coordinator';
import { NativeApiFetcher } from '../native/native-api-fetcher';
import { NativeAuthPlugin, NativeContextHandle } from '../native/native-auth.plugin';

function fixture(overrides: Partial<NativeAuthPlugin> = {}) {
  const events: string[] = [];
  const epoch = new AuthEpoch();
  const plugin: NativeAuthPlugin = {
    invitationHost: vi.fn(async () => ({ host: null })),
    authOptions: vi.fn(async () => ({ googleConfigured: true, developmentLoginEnabled: false })),
    sessionInfo: vi.fn(async () => ({ authenticated: true, userId: '11111111-1111-4111-8111-111111111111', displayName: 'Ana' })),
    signInGoogle: vi.fn(async () => undefined), signInDevelopment: vi.fn(async () => undefined),
    activateRequestContext: vi.fn(async input => { events.push(`activate:${input.userId}:${input.workspaceId}`); return `context-${events.length}` as NativeContextHandle; }),
    prepareRequest: vi.fn(async () => 'permit' as never), dispatchRequest: vi.fn(async () => ({ status: 200, headers: {}, body: '{}' })),
    prepareUnauthorizedRetry: vi.fn(async () => 'retry' as never), beginLogout: vi.fn(async () => { events.push('beginLogout'); return 'attempt' as never; }),
    logoutResult: vi.fn(async () => ({ serverRevoked: true })), clearLocalSession: vi.fn(async () => { events.push('clearLocal'); }),
    ...overrides,
  };
  const fetcher = new NativeApiFetcher(plugin, () => epoch.snapshot());
  const coordinator = new NativeAuthCoordinator(epoch, fetcher, plugin);
  return { coordinator, epoch, fetcher, plugin, events };
}

describe('NativeAuthCoordinator lifecycle', () => {
  it('activates the trusted user context before restore returns and adopts that handle for the first protected fetch', async () => {
    const h = fixture();
    const info = await h.coordinator.restore();
    expect(info.authenticated).toBe(true);
    expect(h.events[0]).toBe('activate:11111111-1111-4111-8111-111111111111:null');
    await h.fetcher.fetcher('/api/me');
    expect(h.plugin.activateRequestContext).toHaveBeenCalledOnce();
  });

  it('Offline_logout_clears_private_state_but_reports_revocation_unconfirmed', async () => {
    let release!: (value: { serverRevoked: boolean }) => void;
    const h = fixture({ logoutResult: vi.fn(() => new Promise(resolve => { release = resolve; })) });
    await h.coordinator.activateWorkspace('user-a', 'space-a');
    let clearedAtEpoch = -1;
    let finished = false;
    const logout = h.coordinator.logout(() => { clearedAtEpoch = h.epoch.snapshot().epoch; h.events.push('ui-cleared'); });
    await Promise.resolve();
    await Promise.resolve();
    expect(h.events).toContain('ui-cleared');
    expect(clearedAtEpoch).toBeGreaterThan(0);
    expect(h.epoch.snapshot()).toMatchObject({ userId: null, workspaceId: null });
    logout.then(() => { finished = true; });
    await Promise.resolve();
    expect(finished).toBe(false);
    release({ serverRevoked: false });
    await expect(logout).resolves.toBe(false);
  });

  it('does not commit an older workspace activation after a newer selection is requested', async () => {
    let finishFirst!: (value: NativeContextHandle) => void;
    const h = fixture({ activateRequestContext: vi.fn()
      .mockImplementationOnce(() => new Promise(resolve => { finishFirst = resolve; }))
      .mockImplementation(async () => 'new-context' as NativeContextHandle) });
    const oldSelection = h.coordinator.activateWorkspace('user-a', 'space-a');
    await Promise.resolve();
    const newSelection = h.coordinator.activateWorkspace('user-a', 'space-b');
    finishFirst('old-context' as NativeContextHandle);
    expect(await oldSelection).toBe(false);
    expect(await newSelection).toBe(true);
    expect(h.epoch.snapshot().workspaceId).toBe('space-b');
  });

  it('expires the local identity after a final protected 401 response', async () => {
    const h = fixture({ dispatchRequest: vi.fn(async () => ({ status: 401, headers: {}, body: '{}' })) });
    const identityLost = vi.fn();
    h.coordinator.setIdentityLostHandler(identityLost);
    await h.coordinator.activateWorkspace('user-a', 'space-a');
    await h.fetcher.fetcher('/api/me');
    expect(h.plugin.clearLocalSession).toHaveBeenCalledOnce();
    expect(h.epoch.snapshot()).toMatchObject({ userId: null, workspaceId: null });
    expect(identityLost).toHaveBeenCalledOnce();
  });

  it('invalidates the selected native context before recovering from a permission loss', async () => {
    const h = fixture({ dispatchRequest: vi.fn(async () => ({ status: 403, headers: {}, body: '{}' })) });
    const permissionLost = vi.fn();
    await h.coordinator.activateWorkspace('user-a', 'space-a');
    h.coordinator.setPermissionLostHandler(permissionLost);
    await h.fetcher.fetcher('/api/finance');
    expect(h.plugin.activateRequestContext).toHaveBeenLastCalledWith({ angularEpoch: 2, userId: 'user-a', workspaceId: null });
    expect(h.epoch.snapshot()).toMatchObject({ userId: 'user-a', workspaceId: null });
    expect(permissionLost).toHaveBeenCalledOnce();
  });
});
