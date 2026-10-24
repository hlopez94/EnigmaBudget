import { Injectable } from '@angular/core';
import { AccountSummary, CardSummary, CompleteDraftRequest, OnboardingState, SaveDraftRequest, SessionInfo } from './api.models';

type Fetcher = typeof fetch;
export class ApiProblem extends Error {
  constructor(readonly status: number, message: string) { super(message); this.name = 'ApiProblem'; }
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private csrfToken = '';
  private readonly fetcher: Fetcher = (...args) => fetch(...args);

  async session(): Promise<SessionInfo> {
    const session = await this.request<SessionInfo>('/api/auth/session');
    this.csrfToken = session.csrfToken;
    return session;
  }
  async signInDevelopment(): Promise<SessionInfo> {
    if (!this.csrfToken) await this.session();
    await this.mutate<void>('/api/auth/development', { method: 'POST', body: '{}' });
    return this.session();
  }
  signInGoogle(): void { window.location.assign('/api/auth/google'); }
  async logout(): Promise<void> {
    await this.mutate<void>('/api/auth/logout', { method: 'POST', body: '{}' });
    this.csrfToken = '';
    await this.session();
  }
  loadOnboarding(): Promise<OnboardingState> { return this.request('/api/onboarding'); }
  saveDraft(draft: SaveDraftRequest): Promise<OnboardingState> {
    return this.mutate('/api/onboarding', { method: 'PUT', body: JSON.stringify(draft) });
  }
  completeDraft(request: CompleteDraftRequest): Promise<OnboardingState> {
    return this.mutate('/api/onboarding/complete', { method: 'POST', body: JSON.stringify(request) });
  }
  accounts(): Promise<AccountSummary[]> { return this.request('/api/accounts'); }
  cards(): Promise<CardSummary[]> { return this.request('/api/cards'); }

  private mutate<T>(url: string, init: RequestInit): Promise<T> {
    return this.request<T>(url, { ...init, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': this.csrfToken } });
  }
  private async request<T>(url: string, init: RequestInit = {}): Promise<T> {
    const response = await this.fetcher(url, { ...init, credentials: 'include' });
    if (!response.ok) {
      let message = `No se pudo completar la solicitud (${response.status}).`;
      try { const problem = await response.json() as { detail?: string; title?: string }; message = problem.detail || problem.title || message; } catch { /* Empty or non-JSON response. */ }
      throw new ApiProblem(response.status, message);
    }
    if (response.status === 204) return undefined as T;
    return response.json() as Promise<T>;
  }
}
