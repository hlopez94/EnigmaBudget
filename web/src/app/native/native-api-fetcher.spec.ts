import { describe, expect, it, vi } from 'vitest';
import { NativeApiFetcher, NativeRequestChangedError, NativeRequestSnapshot } from './native-api-fetcher';
import { NativeAuthPlugin, NativeContextHandle, NativeDispatchPermit } from './native-auth.plugin';

const context = 'context' as NativeContextHandle;
const permit = 'permit' as NativeDispatchPermit;
const invalidPermit = 'retry' as NativeDispatchPermit;

function harness() {
  let snapshot: NativeRequestSnapshot = { epoch: 1, userId: 'user-1', workspaceId: 'workspace-1' };
  const plugin: NativeAuthPlugin = {
    authOptions: vi.fn(async () => ({ googleConfigured: false, developmentLoginEnabled: true })),
    sessionInfo: vi.fn(async () => ({ authenticated: true, userId: 'user-1', displayName: 'Ana' })),
    signInGoogle: vi.fn(async () => undefined),
    signInDevelopment: vi.fn(async () => undefined),
    activateRequestContext: vi.fn(async () => context),
    prepareRequest: vi.fn(async () => permit),
    dispatchRequest: vi.fn(async () => ({ status: 200, headers: { 'content-type': 'application/json' }, body: '{"ok":true}' })),
    prepareUnauthorizedRetry: vi.fn(async () => invalidPermit),
    beginLogout: vi.fn(async () => 'attempt' as never),
    logoutResult: vi.fn(async () => ({ serverRevoked: true })),
    clearLocalSession: vi.fn(async () => undefined),
  };
  return { plugin, reader: () => snapshot, change: (next: NativeRequestSnapshot) => { snapshot = next; } };
}

describe('NativeApiFetcher', () => {
  it('rechecks the local snapshot after prepareRequest before dispatch', async () => {
    const h = harness();
    h.plugin.prepareRequest = vi.fn(async () => {
      h.change({ epoch: 2, userId: 'user-2', workspaceId: 'workspace-2' });
      return permit;
    });
    const fetcher = new NativeApiFetcher(h.plugin, h.reader);
    await expect(fetcher.fetcher('/api/finance')).rejects.toBeInstanceOf(NativeRequestChangedError);
    expect(h.plugin.dispatchRequest).not.toHaveBeenCalled();
  });

  it('rechecks after preparing an unauthorized retry and never dispatches it after a context change', async () => {
    const h = harness();
    h.plugin.dispatchRequest = vi.fn(async () => ({ status: 401, headers: { 'www-authenticate': 'Bearer error="invalid_token"' }, body: '{}' }));
    h.plugin.prepareUnauthorizedRetry = vi.fn(async () => {
      h.change({ epoch: 3, userId: 'user-1', workspaceId: 'workspace-2' });
      return invalidPermit;
    });
    const fetcher = new NativeApiFetcher(h.plugin, h.reader);
    await expect(fetcher.fetcher('/api/finance')).rejects.toBeInstanceOf(NativeRequestChangedError);
    expect(h.plugin.dispatchRequest).toHaveBeenCalledTimes(1);
  });

  it('only asks for the one retry after the native response identifies invalid_token', async () => {
    const h = harness();
    h.plugin.dispatchRequest = vi.fn()
      .mockResolvedValueOnce({ status: 401, headers: { 'www-authenticate': 'Bearer error="invalid_token"' }, body: '{}' })
      .mockResolvedValueOnce({ status: 200, headers: { 'content-type': 'application/json' }, body: '{"ok":true}' });
    const fetcher = new NativeApiFetcher(h.plugin, h.reader);
    const result = await fetcher.fetcher('/api/finance');
    expect(result.status).toBe(200);
    expect(h.plugin.prepareUnauthorizedRetry).toHaveBeenCalledOnce();
    expect(h.plugin.dispatchRequest).toHaveBeenCalledTimes(2);
  });

  it('does not retry an ordinary endpoint 401', async () => {
    const h = harness();
    h.plugin.dispatchRequest = vi.fn(async () => ({ status: 401, headers: { 'content-type': 'application/json' }, body: '{}' }));
    const fetcher = new NativeApiFetcher(h.plugin, h.reader);
    expect((await fetcher.fetcher('/api/finance')).status).toBe(401);
    expect(h.plugin.prepareUnauthorizedRetry).not.toHaveBeenCalled();
  });

  it('maps sessionInfo to the credential-free public shape', async () => {
    const h = harness();
    const result = await new NativeApiFetcher(h.plugin, h.reader).sessionInfo();
    expect(result).toEqual({ authenticated: true, userId: 'user-1', displayName: 'Ana' });
    expect(result).not.toHaveProperty('accessToken');
    expect(result).not.toHaveProperty('refreshToken');
  });

  it('retains the normal web fetcher as the API_FETCHER default', async () => {
    const original = globalThis.fetch;
    const webFetch = vi.fn(async () => new Response('{"ok":true}', { status: 200 }));
    globalThis.fetch = webFetch as typeof fetch;
    try {
      const { API_FETCHER, ApiService } = await import('../core/api.service');
      expect(API_FETCHER).toBeDefined();
      await new ApiService().me();
      expect(webFetch).toHaveBeenCalledOnce();
      expect(webFetch).toHaveBeenCalledWith('/api/me', expect.objectContaining({ credentials: 'include' }));
    } finally {
      globalThis.fetch = original;
    }
  });
});
