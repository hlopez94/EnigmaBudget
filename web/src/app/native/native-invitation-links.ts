import { App } from '@capacitor/app';
import { Capacitor } from '@capacitor/core';
import type { PluginListenerHandle } from '@capacitor/core';

/** Keeps a native invitation fragment in memory until the authenticated preview flow can consume it. */
export class NativeInvitationLinks {
  private listener: PluginListenerHandle | null = null;
  private pendingToken: string | null = null;
  private authenticated = false;
  private expectedHost: string | null = null;
  private deliver: ((token: string) => Promise<void> | void) | null = null;
  private generation = 0;
  private authGeneration = 0;
  private startTask: Promise<void> | null = null;
  private readonly pendingStarts = new Set<Promise<void>>();

  async start(expectedHost: string | null, onAuthenticatedInvite: (token: string) => Promise<void> | void): Promise<void> {
    if (!Capacitor.isNativePlatform() || !expectedHost) return;
    const normalizedHost = expectedHost.toLowerCase();
    if (this.listener && this.expectedHost === normalizedHost && this.deliver === onAuthenticatedInvite) return;
    const generation = ++this.generation;
    await this.listener?.remove().catch(() => undefined);
    this.listener = null;
    this.pendingToken = null;
    if (generation !== this.generation) return;
    this.expectedHost = normalizedHost;
    this.deliver = onAuthenticatedInvite;
    const starting = (async () => {
      const listener = await App.addListener('appUrlOpen', ({ url }) => this.capture(url));
      if (generation !== this.generation) { await listener.remove(); return; }
      this.listener = listener;
      const launch = await App.getLaunchUrl();
      if (generation === this.generation && launch?.url) this.capture(launch.url);
    })();
    this.startTask = starting;
    this.pendingStarts.add(starting);
    try { await starting; }
    finally {
      this.pendingStarts.delete(starting);
      if (this.startTask === starting) this.startTask = null;
    }
  }

  setAuthenticated(authenticated: boolean): void {
    if (this.authenticated !== authenticated) ++this.authGeneration;
    this.authenticated = authenticated;
    if (!authenticated) return;
    void this.deliverPending();
  }

  clearPendingInvitation(): void {
    ++this.authGeneration;
    this.pendingToken = null;
  }

  async stop(): Promise<void> {
    ++this.generation;
    ++this.authGeneration;
    const listener = this.listener;
    this.listener = null;
    this.pendingToken = null;
    this.deliver = null;
    this.expectedHost = null;
    this.authenticated = false;
    await listener?.remove().catch(() => undefined);
    await Promise.allSettled([...this.pendingStarts]);
  }

  private capture(rawUrl: string): void {
    let url: URL;
    try { url = new URL(rawUrl); } catch { return; }
    if (url.protocol !== 'https:' || url.hostname.toLowerCase() !== this.expectedHost || url.pathname !== '/invitar' || url.search) return;
    const fragment = new URLSearchParams(url.hash.slice(1));
    const token = fragment.get('invite');
    if (!token || fragment.size !== 1) return;
    this.pendingToken = token;
    // The fragment is secret-bearing. Clear the visible WebView URL before invoking application code.
    try { window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}`); }
    catch { this.pendingToken = null; return; }
    if (this.authenticated) void this.deliverPending();
  }

  private async deliverPending(): Promise<void> {
    const token = this.pendingToken;
    const deliver = this.deliver;
    if (!token || !deliver || !this.authenticated) return;
    const authGeneration = this.authGeneration;
    this.pendingToken = null;
    try { await deliver(token); }
    catch { if (this.authenticated && authGeneration === this.authGeneration && this.pendingToken === null) this.pendingToken = token; }
  }
}
