import { registerPlugin } from '@capacitor/core';

declare const contextBrand: unique symbol;
declare const permitBrand: unique symbol;
declare const logoutBrand: unique symbol;

export type NativeContextHandle = string & { readonly [contextBrand]: true };
export type NativeDispatchPermit = string & { readonly [permitBrand]: true };
export type NativeLogoutAttempt = string & { readonly [logoutBrand]: true };

export interface NativeAuthOptions {
  googleConfigured: boolean;
  developmentLoginEnabled: boolean;
}

export interface NativeSessionInfo {
  authenticated: boolean;
  userId: string | null;
  displayName: string | null;
}

export interface NativeInvitationHost { host: string | null; }

export interface NativeApiRequest {
  url: string;
  method: string;
  headers: Record<string, string>;
  body?: string;
}

export interface NativeApiResponse {
  status: number;
  headers: Record<string, string>;
  body: string;
}

export interface NativeAuthPlugin {
  invitationHost(): Promise<NativeInvitationHost>;
  authOptions(): Promise<NativeAuthOptions>;
  sessionInfo(): Promise<NativeSessionInfo>;
  signInGoogle(): Promise<void>;
  signInDevelopment(key?: string): Promise<void>;
  activateRequestContext(input: { angularEpoch: number; userId: string; workspaceId: string | null }): Promise<NativeContextHandle>;
  prepareRequest(input: { context: NativeContextHandle; requestId: string; request: NativeApiRequest }): Promise<NativeDispatchPermit>;
  dispatchRequest(permit: NativeDispatchPermit): Promise<NativeApiResponse>;
  prepareUnauthorizedRetry(input: { context: NativeContextHandle; requestId: string; priorPermit: NativeDispatchPermit }): Promise<NativeDispatchPermit>;
  beginLogout(): Promise<NativeLogoutAttempt>;
  logoutResult(attempt: NativeLogoutAttempt): Promise<{ serverRevoked: boolean }>;
  clearLocalSession(): Promise<void>;
}

interface NativeAuthBridge {
  invitationHost(): Promise<NativeInvitationHost>;
  authOptions(): Promise<NativeAuthOptions>;
  sessionInfo(): Promise<NativeSessionInfo>;
  signInGoogle(): Promise<void>;
  signInDevelopment(options?: { key?: string }): Promise<void>;
  activateRequestContext(input: { angularEpoch: number; userId: string; workspaceId: string | null }): Promise<{ value: string }>;
  prepareRequest(input: { context: string; requestId: string; request: NativeApiRequest }): Promise<{ value: string }>;
  dispatchRequest(input: { permit: string }): Promise<NativeApiResponse>;
  prepareUnauthorizedRetry(input: { context: string; requestId: string; priorPermit: string }): Promise<{ value: string }>;
  beginLogout(): Promise<{ value: string }>;
  logoutResult(input: { attempt: string }): Promise<{ serverRevoked: boolean }>;
  clearLocalSession(): Promise<void>;
}

const bridge = registerPlugin<NativeAuthBridge>('NativeAuth');
export const NativeAuth: NativeAuthPlugin = {
  invitationHost: () => bridge.invitationHost(),
  authOptions: () => bridge.authOptions(),
  sessionInfo: () => bridge.sessionInfo(),
  signInGoogle: () => bridge.signInGoogle(),
  signInDevelopment: (key) => bridge.signInDevelopment(key === undefined ? {} : { key }),
  activateRequestContext: async (input) => (await bridge.activateRequestContext(input)).value as NativeContextHandle,
  prepareRequest: async (input) => (await bridge.prepareRequest({ ...input, context: input.context })).value as NativeDispatchPermit,
  dispatchRequest: (permit) => bridge.dispatchRequest({ permit }),
  prepareUnauthorizedRetry: async (input) => (await bridge.prepareUnauthorizedRetry({ ...input, context: input.context, priorPermit: input.priorPermit })).value as NativeDispatchPermit,
  beginLogout: async () => (await bridge.beginLogout()).value as NativeLogoutAttempt,
  logoutResult: (attempt) => bridge.logoutResult({ attempt }),
  clearLocalSession: () => bridge.clearLocalSession(),
};
