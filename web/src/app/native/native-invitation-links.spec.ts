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
import { App } from '@capacitor/app';

describe('NativeInvitationLinks', () => {
  beforeEach(() => {
    native.isPlatform = true;
    native.callback = null;
    native.launchUrl = null;
    native.remove.mockClear();
    vi.mocked(App.addListener).mockClear();
    vi.mocked(App.getLaunchUrl).mockClear();
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

  it('keeps one listener across repeated start calls and removes the previous host on restart', async () => {
    const received: string[] = [];
    const callback = (token: string) => received.push(token);
    const links = new NativeInvitationLinks();
    await links.start('invites.example.test', callback);
    const oldListenerCallback = native.callback;
    await links.start('invites.example.test', callback);
    expect(App.addListener).toHaveBeenCalledTimes(1);
    await links.start('second.example.test', callback);
    expect(native.remove).toHaveBeenCalledOnce();
    oldListenerCallback?.({ url: 'https://invites.example.test/invitar#invite=old-host' });
    native.callback?.({ url: 'https://second.example.test/invitar#invite=new-host' });
    links.setAuthenticated(true);
    await Promise.resolve();
    expect(received).toEqual(['new-host']);
    await links.stop();
    await links.stop();
    expect(native.remove).toHaveBeenCalledTimes(2);
  });

  it('removes a listener whose registration finishes after stop was requested', async () => {
    let finishRegistration!: (value: never) => void;
    const registration = new Promise<never>(resolve => { finishRegistration = resolve; });
    vi.mocked(App.addListener).mockImplementationOnce(() => registration);
    const removeLateListener = vi.fn(async () => undefined);
    const links = new NativeInvitationLinks();
    const starting = links.start('invites.example.test', () => undefined);
    await Promise.resolve();
    const stopping = links.stop();
    finishRegistration({ remove: removeLateListener } as never);
    await Promise.all([starting, stopping]);
    expect(removeLateListener).toHaveBeenCalledOnce();
    expect(App.getLaunchUrl).not.toHaveBeenCalled();
  });

  it('does not restore a failed invitation delivery after logout changes identity generation', async () => {
    let rejectDelivery!: (reason: Error) => void;
    const delivery = new Promise<void>((_resolve, reject) => { rejectDelivery = reject; });
    const receive = vi.fn(() => delivery);
    const links = new NativeInvitationLinks();
    await links.start('invites.example.test', receive);
    native.callback?.({ url: 'https://invites.example.test/invitar#invite=private-token' });
    links.setAuthenticated(true);
    expect(receive).toHaveBeenCalledOnce();
    links.setAuthenticated(false);
    rejectDelivery(new Error('old identity'));
    await Promise.resolve();
    links.setAuthenticated(true);
    await Promise.resolve();
    expect(receive).toHaveBeenCalledOnce();
    await links.stop();
  });
});
