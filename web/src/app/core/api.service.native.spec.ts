import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@capacitor/core', () => ({ Capacitor: { getPlatform: () => 'android', isNativePlatform: () => true }, registerPlugin: () => ({}) }));

import { ApiService } from './api.service';

describe('native API service adapter', () => {
  beforeEach(() => sessionStorage.clear());

  it('routes native auth through the coordinator and keeps invitations memory-only', async () => {
    const fetcher = vi.fn(async () => new Response('{}'));
    const coordinator = {
      restore: vi.fn(async () => ({ authenticated: false, displayName: null, googleConfigured: true, developmentLoginEnabled: false, developmentUsers: [], csrfToken: '' })),
      signInDevelopment: vi.fn(async () => undefined), signInGoogle: vi.fn(async () => undefined),
    };
    const getItem = vi.spyOn(Storage.prototype, 'getItem');
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    const removeItem = vi.spyOn(Storage.prototype, 'removeItem');
    const api = new ApiService(fetcher as typeof fetch, coordinator as never);

    api.captureNativeInvitation('temporary-invite');
    expect(api.capturePendingInvitation()).toBe('temporary-invite');
    api.clearPendingInvitation();
    expect(api.capturePendingInvitation()).toBeNull();
    await api.session();
    await api.signInDevelopment('development-key');
    await api.signInGoogle();

    expect(coordinator.restore).toHaveBeenCalledTimes(2);
    expect(coordinator.signInDevelopment).toHaveBeenCalledWith('development-key', expect.any(Function));
    expect(coordinator.signInGoogle).toHaveBeenCalledWith(expect.any(Function));
    expect(fetcher).not.toHaveBeenCalled();
    expect(getItem).not.toHaveBeenCalled();
    expect(setItem).not.toHaveBeenCalled();
    expect(removeItem).not.toHaveBeenCalled();
  });
});
