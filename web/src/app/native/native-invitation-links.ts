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

  async start(expectedHost: string | null, onAuthenticatedInvite: (token: string) => Promise<void> | void): Promise<void> {
    if (!Capacitor.isNativePlatform() || !expectedHost) return;
    this.expectedHost = expectedHost.toLowerCase();
    this.deliver = onAuthenticatedInvite;
    this.listener = await App.addListener('appUrlOpen', ({ url }) => this.capture(url));
    const launch = await App.getLaunchUrl();
    if (launch?.url) this.capture(launch.url);
  }

  setAuthenticated(authenticated: boolean): void {
    this.authenticated = authenticated;
    if (authenticated) void this.deliverPending();
  }

  async stop(): Promise<void> {
    await this.listener?.remove();
    this.listener = null;
    this.pendingToken = null;
    this.deliver = null;
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
    this.pendingToken = null;
    try { await deliver(token); }
    catch { if (this.pendingToken === null) this.pendingToken = token; }
  }
}
