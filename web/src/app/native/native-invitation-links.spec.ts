import { beforeEach, describe, expect, it, vi } from 'vitest';

const native = vi.hoisted(() => ({
  isPlatform: true,
  callback: null as null | ((event: { url: string }) => void),
  remove: vi.fn(async () => undefined),
  launchUrl: null as string | null,
}));

vi.mock('@capacitor/core', () => ({ Capacitor: { isNativePlatform: () => native.isPlatform } }));
vi.mock('@capacitor/app', () => ({ App: {
  addListener: vi.fn(async (_name: string, callback: (event: { url: string }) => void) => {
    native.callback = callback;
    return { remove: native.remove };
  }),
  getLaunchUrl: vi.fn(async () => native.launchUrl ? { url: native.launchUrl } : {}),
} }));

import { NativeInvitationLinks } from './native-invitation-links';

describe('NativeInvitationLinks', () => {
  beforeEach(() => {
    native.isPlatform = true;
    native.callback = null;
    native.launchUrl = null;
    native.remove.mockClear();
    window.history.replaceState(null, '', '/');
  });

  it('captures only the configured HTTPS host and /invitar fragment, clears the URL, and waits for auth', async () => {
    const received: string[] = [];
    const listener = new NativeInvitationLinks();
    await listener.start('invites.example.test', token => received.push(token));
    native.callback?.({ url: 'https://invites.example.test/invitar#invite=private-fragment' });
    expect(window.location.href).not.toContain('private-fragment');
    expect(received).toEqual([]);
    listener.setAuthenticated(true);
    await Promise.resolve();
    expect(received).toEqual(['private-fragment']);
    await listener.stop();
    expect(native.remove).toHaveBeenCalledOnce();
  });

  it('rejects a different host, path, protocol, query, or extra fragment fields', async () => {
    const received: string[] = [];
    const listener = new NativeInvitationLinks();
    await listener.start('invites.example.test', token => received.push(token));
    listener.setAuthenticated(true);
    for (const url of [
      'https://other.example.test/invitar#invite=x',
      'https://invites.example.test/other#invite=x',
      'http://invites.example.test/invitar#invite=x',
      'https://invites.example.test/invitar?token=x#invite=x',
      'https://invites.example.test/invitar#invite=x&other=y',
    ]) native.callback?.({ url });
    expect(received).toEqual([]);
  });

  it('captures an initial launch URL after registering the event listener', async () => {
    const received: string[] = [];
    native.launchUrl = 'https://invites.example.test/invitar#invite=cold-start-secret';
    const listener = new NativeInvitationLinks();
    await listener.start('invites.example.test', token => received.push(token));
    expect(window.location.href).not.toContain('cold-start-secret');
    listener.setAuthenticated(true);
    await Promise.resolve();
    expect(received).toEqual(['cold-start-secret']);
  });

  it('does not register when no host is configured or the app is not native', async () => {
    const listener = new NativeInvitationLinks();
    await listener.start(null, () => undefined);
    expect(native.callback).toBeNull();
    native.isPlatform = false;
    await listener.start('invites.example.test', () => undefined);
    expect(native.callback).toBeNull();
  });
});
