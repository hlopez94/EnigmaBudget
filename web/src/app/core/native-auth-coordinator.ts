import { Inject, Injectable, Optional } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { SessionInfo } from './api.models';
import { AuthEpoch } from './auth-epoch';
import { NativeApiFetcher, NATIVE_AUTH_PLUGIN } from '../native/native-api-fetcher';
import { NativeAuth, NativeAuthPlugin, NativeInvitationHost } from '../native/native-auth.plugin';

/** Coordinates native identity/context changes before Angular publishes the resulting state. */
@Injectable({ providedIn: 'root' })
export class NativeAuthCoordinator {
  private restoreFlight: Promise<SessionInfo> | null = null;
  private cachedSession: SessionInfo | null = null;
  private transitionTail: Promise<void> = Promise.resolve();
  private transitionVersion = 0;
  private identityGeneration = 0;
  private identityLostHandler: (() => void) | null = null;
  private permissionLostHandler: (() => void) | null = null;
  private readonly plugin: NativeAuthPlugin;

  constructor(private readonly epoch: AuthEpoch, private readonly fetcher: NativeApiFetcher, @Optional() @Inject(NATIVE_AUTH_PLUGIN) plugin?: NativeAuthPlugin) {
    this.plugin = plugin ?? NativeAuth;
    this.fetcher.setUnauthorizedHandler(() => this.clearIdentity());
    this.fetcher.setForbiddenHandler(() => this.invalidateWorkspaceAfterPermissionLoss());
  }

  setIdentityLostHandler(handler: (() => void) | null): void { this.identityLostHandler = handler; }
  setPermissionLostHandler(handler: (() => void) | null): void { this.permissionLostHandler = handler; }

  get isNative(): boolean { return Capacitor.getPlatform() === 'android'; }

  async authOptions(): Promise<{ googleConfigured: boolean; developmentLoginEnabled: boolean }> {
    const options = await this.plugin.authOptions();
    return { googleConfigured: options.googleConfigured === true, developmentLoginEnabled: options.developmentLoginEnabled === true };
  }

  restore(): Promise<SessionInfo> {
    if (this.cachedSession) return Promise.resolve(this.cachedSession);
    if (!this.restoreFlight) {
      const pending = this.restoreNative(this.identityGeneration);
      this.restoreFlight = pending;
      void pending.then(
        () => { if (this.restoreFlight === pending) this.restoreFlight = null; },
        () => { if (this.restoreFlight === pending) this.restoreFlight = null; },
      );
    }
    return this.restoreFlight;
  }

  async signInGoogle(onInvalidated: () => void = () => undefined): Promise<void> {
    await this.prepareIdentityChange(onInvalidated);
    await this.plugin.signInGoogle();
  }

  async signInDevelopment(key?: string, onInvalidated: () => void = () => undefined): Promise<void> {
    await this.prepareIdentityChange(onInvalidated);
    await this.plugin.signInDevelopment(key);
  }

  async logout(onLocalClear: () => void): Promise<boolean> {
    const attempt = await this.plugin.beginLogout();
    ++this.identityGeneration;
    this.restoreFlight = null;
    this.cachedSession = null;
    this.epoch.advance(null, null);
    this.fetcher.invalidateContext();
    onLocalClear();
    const result = await this.plugin.logoutResult(attempt);
    return result.serverRevoked === true;
  }

  async activateWorkspace(userId: string, workspaceId: string | null, force = false): Promise<boolean> {
    const active = this.epoch.snapshot();
    if (!force && active.userId === userId && active.workspaceId === workspaceId) return true;
    const version = ++this.transitionVersion;
    let result = false;
    const task = this.transitionTail.then(async () => {
      if (version !== this.transitionVersion) return;
      const current = this.epoch.snapshot();
      const nextEpoch = current.epoch + 1;
      const handle = await this.plugin.activateRequestContext({ angularEpoch: nextEpoch, userId, workspaceId });
      if (version !== this.transitionVersion) return;
      this.epoch.advance(userId, workspaceId);
      this.fetcher.adoptContext(this.epoch.snapshot(), handle);
      result = true;
    });
    this.transitionTail = task.catch(() => undefined);
    await task;
    return result;
  }

  private async invalidateWorkspaceAfterPermissionLoss(): Promise<void> {
    const userId = this.epoch.snapshot().userId;
    if (userId) await this.activateWorkspace(userId, null, true);
    this.permissionLostHandler?.();
  }

  async prepareForegroundRestore(userId: string): Promise<boolean> {
    this.cachedSession = null;
    return this.activateWorkspace(userId, null, true);
  }

  async clearIdentity(): Promise<void> {
    const generation = ++this.identityGeneration;
    this.restoreFlight = null;
    this.cachedSession = null;
    ++this.transitionVersion;
    await this.plugin.clearLocalSession();
    if (generation !== this.identityGeneration) return;
    this.epoch.advance(null, null);
    this.fetcher.invalidateContext();
    this.identityLostHandler?.();
  }

  invitationHost(): Promise<NativeInvitationHost> { return this.plugin.invitationHost(); }

  private async prepareIdentityChange(onInvalidated: () => void): Promise<void> {
    await this.plugin.clearLocalSession();
    ++this.identityGeneration;
    this.restoreFlight = null;
    this.cachedSession = null;
    ++this.transitionVersion;
    this.fetcher.invalidateContext();
    this.epoch.advance(null, null);
    onInvalidated();
  }

  private async restoreNative(generation: number): Promise<SessionInfo> {
    const [options, native] = await Promise.all([this.authOptions(), this.fetcher.sessionInfo()]);
    if (generation !== this.identityGeneration) throw new Error('La sesión cambió mientras comprobábamos el acceso.');
    if (!native.authenticated || !native.userId || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(native.userId)) {
      this.epoch.advance(null, null);
      this.fetcher.invalidateContext();
      this.cachedSession = { authenticated: false, displayName: null, googleConfigured: options.googleConfigured, developmentLoginEnabled: options.developmentLoginEnabled, developmentUsers: [], csrfToken: '' };
      return this.cachedSession;
    }
    const current = this.epoch.snapshot();
    const activated = current.userId === native.userId && current.workspaceId === null
      ? true
      : await this.activateWorkspace(native.userId, null);
    if (!activated) throw new Error('La sesión cambió mientras preparábamos tu espacio.');
    this.cachedSession = { authenticated: true, displayName: native.displayName, googleConfigured: options.googleConfigured, developmentLoginEnabled: options.developmentLoginEnabled, developmentUsers: [], csrfToken: '' };
    return this.cachedSession;
  }
}
