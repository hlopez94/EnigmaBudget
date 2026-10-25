import { Inject, Injectable, InjectionToken, Optional } from '@angular/core';
import { NativeApiRequest, NativeApiResponse, NativeAuth, NativeAuthPlugin, NativeContextHandle, NativeDispatchPermit, NativeSessionInfo } from './native-auth.plugin';

export interface NativeRequestSnapshot {
  epoch: number;
  userId: string | null;
  workspaceId: string | null;
}

export type NativeSnapshotReader = () => NativeRequestSnapshot;
export const NATIVE_AUTH_PLUGIN = new InjectionToken<NativeAuthPlugin>('NATIVE_AUTH_PLUGIN');
export const NATIVE_SNAPSHOT_READER = new InjectionToken<NativeSnapshotReader>('NATIVE_SNAPSHOT_READER');

export class NativeRequestChangedError extends Error {
  constructor() {
    super('La sesión o el espacio cambió durante la solicitud. Volvé a cargar los datos.');
    this.name = 'NativeRequestChangedError';
  }
}

/** Bridges the existing API_FETCHER contract without exposing native credentials to JavaScript. */
@Injectable({ providedIn: 'root' })
export class NativeApiFetcher {
  private context: { key: string; handle: NativeContextHandle } | null = null;
  private contextPromise: { key: string; promise: Promise<NativeContextHandle> } | null = null;
  private nextRequest = 0;

  private readonly plugin: NativeAuthPlugin;
  private readonly readSnapshot: NativeSnapshotReader;
  private unauthorizedHandler: (() => Promise<void> | void) | null = null;
  private forbiddenHandler: (() => Promise<void> | void) | null = null;

  constructor(
    @Optional() @Inject(NATIVE_AUTH_PLUGIN) plugin?: NativeAuthPlugin,
    @Optional() @Inject(NATIVE_SNAPSHOT_READER) readSnapshot?: NativeSnapshotReader,
  ) {
    this.plugin = plugin ?? NativeAuth;
    this.readSnapshot = readSnapshot ?? (() => ({ epoch: 0, userId: null, workspaceId: null }));
  }

  async sessionInfo(): Promise<NativeSessionInfo> {
    const result = await this.plugin.sessionInfo();
    return { authenticated: result.authenticated, userId: result.userId, displayName: result.displayName };
  }

  /** Cache the context already activated by NativeAuthCoordinator to avoid a duplicate invalidation on the first API call. */
  adoptContext(snapshot: NativeRequestSnapshot, handle: NativeContextHandle): void {
    this.context = { key: this.contextKey(snapshot), handle };
    this.contextPromise = null;
  }

  setUnauthorizedHandler(handler: (() => Promise<void> | void) | null): void { this.unauthorizedHandler = handler; }
  setForbiddenHandler(handler: (() => Promise<void> | void) | null): void { this.forbiddenHandler = handler; }

  readonly fetcher: typeof fetch = async (input, init) => {
    const url = input instanceof Request ? input.url : String(input);
    if (/^\/api\/auth(?:\/|$)/i.test(new URL(url, this.requestBase()).pathname)) {
      throw new TypeError('La autenticación nativa solo puede usar el puente privado.');
    }
    const requestInit = input instanceof Request ? new Request(input, init) : new Request(new URL(url, this.requestBase()).toString(), init);
    const snapshot = this.readSnapshot();
    if (!snapshot.userId) throw new NativeRequestChangedError();
    const context = await this.ensureContext(snapshot);
    this.assertCurrent(snapshot);
    const request: NativeApiRequest = {
      url,
      method: requestInit.method,
      headers: this.requestHeaders(requestInit.headers, snapshot),
      ...(requestInit.body === null ? {} : { body: await requestInit.text() }),
    };
    const requestId = `angular-${++this.nextRequest}`;
    let permit = await this.plugin.prepareRequest({ context, requestId, request });
    this.assertCurrent(snapshot);
    let response = await this.dispatchIfCurrent(snapshot, permit);
    if (response.status === 401 && this.isInvalidToken(response)) {
      const priorPermit = permit;
      permit = await this.plugin.prepareUnauthorizedRetry({ context, requestId, priorPermit });
      this.assertCurrent(snapshot);
      response = await this.dispatchIfCurrent(snapshot, permit);
    }
    if (response.status === 401) await this.unauthorizedHandler?.();
    if (response.status === 403) {
      this.assertCurrent(snapshot);
      await this.forbiddenHandler?.();
    }
    return new Response(response.status === 204 ? null : response.body, { status: response.status, headers: response.headers });
  };

  invalidateContext(): void {
    this.context = null;
    this.contextPromise = null;
  }

  private async ensureContext(snapshot: NativeRequestSnapshot): Promise<NativeContextHandle> {
    const key = this.contextKey(snapshot);
    if (this.context?.key === key) return this.context.handle;
    if (!this.contextPromise) {
      const activation = this.plugin.activateRequestContext({ angularEpoch: snapshot.epoch, userId: snapshot.userId!, workspaceId: snapshot.workspaceId });
      const pending = { key, promise: activation };
      this.contextPromise = pending;
      try {
        const handle = await activation;
        this.context = { key, handle };
        return handle;
      } finally {
        if (this.contextPromise === pending) this.contextPromise = null;
      }
    }
    const pending = this.contextPromise;
    const handle = await pending.promise;
    this.assertCurrent(snapshot);
    if (pending.key === key) return handle;
    return this.ensureContext(snapshot);
  }

  private contextKey(snapshot: NativeRequestSnapshot): string {
    return `${snapshot.epoch}\u0000${snapshot.userId}\u0000${snapshot.workspaceId ?? ''}`;
  }

  private requestHeaders(headers: Headers, snapshot: NativeRequestSnapshot): Record<string, string> {
    const result = Object.fromEntries(headers.entries());
    for (const name of Object.keys(result)) if (name.toLowerCase() === 'x-workspace-id') delete result[name];
    if (snapshot.workspaceId) result['X-Workspace-Id'] = snapshot.workspaceId;
    return result;
  }

  private async dispatchIfCurrent(snapshot: NativeRequestSnapshot, permit: NativeDispatchPermit): Promise<NativeApiResponse> {
    this.assertCurrent(snapshot);
    return this.plugin.dispatchRequest(permit);
  }

  private assertCurrent(snapshot: NativeRequestSnapshot): void {
    const current = this.readSnapshot();
    if (current.epoch !== snapshot.epoch || current.userId !== snapshot.userId || current.workspaceId !== snapshot.workspaceId) throw new NativeRequestChangedError();
  }

  private isInvalidToken(response: NativeApiResponse): boolean {
    return Object.entries(response.headers).some(([name, value]) => name.toLowerCase() === 'www-authenticate' && /Bearer\s+error="invalid_token"/i.test(value));
  }

  private requestBase(): string {
    if (typeof window === 'undefined') return 'https://localhost/';
    return window.location.protocol === 'http:' || window.location.protocol === 'https:' ? window.location.href : 'https://localhost/';
  }
}
