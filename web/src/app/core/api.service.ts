import { Injectable } from '@angular/core';
import {
  Account,
  AccountSummary,
  Card,
  CardSummary,
  CompleteDraftRequest,
  FinanceOverview,
  Group,
  OnboardingState,
  Operation,
  Purchase,
  Receivable,
  SaveDraftRequest,
  SessionInfo,
  Statement,
} from './api.models';

type Fetcher = typeof fetch;
export class ApiProblem extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiProblem';
  }
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
  signInGoogle(): void {
    window.location.assign('/api/auth/google');
  }
  async logout(): Promise<void> {
    await this.mutate<void>('/api/auth/logout', { method: 'POST', body: '{}' });
    this.csrfToken = '';
    await this.session();
  }
  loadOnboarding(): Promise<OnboardingState> {
    return this.request('/api/onboarding');
  }
  saveDraft(draft: SaveDraftRequest): Promise<OnboardingState> {
    return this.mutate('/api/onboarding', { method: 'PUT', body: JSON.stringify(draft) });
  }
  completeDraft(request: CompleteDraftRequest): Promise<OnboardingState> {
    return this.mutate('/api/onboarding/complete', {
      method: 'POST',
      body: JSON.stringify(request),
    });
  }
  accounts(): Promise<AccountSummary[]> {
    return this.request('/api/accounts');
  }
  cards(): Promise<CardSummary[]> {
    return this.request('/api/cards');
  }

  financeOverview(period: string): Promise<FinanceOverview> {
    return this.request(`/api/finance?period=${encodeURIComponent(period)}`);
  }
  operations(period: string, accountId?: string): Promise<Operation[]> {
    const query = new URLSearchParams({ period });
    if (accountId) query.set('accountId', accountId);
    return this.request(`/api/operations?${query}`);
  }
  createAccount(input: Record<string, unknown>): Promise<Account> {
    return this.mutate('/api/accounts', { method: 'POST', body: JSON.stringify(input) });
  }
  updateAccount(id: string, input: Record<string, unknown>): Promise<Account> {
    return this.mutate(`/api/accounts/${id}`, { method: 'PUT', body: JSON.stringify(input) });
  }
  adjustAccount(id: string, input: Record<string, unknown>): Promise<Account> {
    return this.mutate(`/api/accounts/${id}/adjustments`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  createOperation(input: Record<string, unknown>): Promise<Operation> {
    return this.mutate('/api/operations', { method: 'POST', body: JSON.stringify(input) });
  }
  payOperation(id: string, input: Record<string, unknown>): Promise<Operation> {
    return this.mutate(`/api/operations/${id}/payments`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  voidOperation(id: string, input: Record<string, unknown>): Promise<Operation> {
    return this.mutate(`/api/operations/${id}/void`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  transfer(input: Record<string, unknown>): Promise<unknown> {
    return this.mutate('/api/transfers', { method: 'POST', body: JSON.stringify(input) });
  }
  createCard(input: Record<string, unknown>): Promise<Card> {
    return this.mutate('/api/cards', { method: 'POST', body: JSON.stringify(input) });
  }
  updateCard(id: string, input: Record<string, unknown>): Promise<Card> {
    return this.mutate(`/api/cards/${id}`, { method: 'PUT', body: JSON.stringify(input) });
  }
  purchases(cardId: string): Promise<Purchase[]> {
    return this.request(`/api/cards/${cardId}/purchases`);
  }
  createPurchase(input: Record<string, unknown>): Promise<Purchase> {
    return this.mutate('/api/purchases', { method: 'POST', body: JSON.stringify(input) });
  }
  statement(cardId: string, period: string, currency: string): Promise<Statement> {
    return this.request(
      `/api/cards/${cardId}/statement?period=${encodeURIComponent(period)}&currency=${currency}`,
    );
  }
  saveStatement(cardId: string, input: Record<string, unknown>): Promise<Statement> {
    return this.mutate(`/api/cards/${cardId}/statement`, {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }
  confirmStatement(cardId: string, input: Record<string, unknown>): Promise<Statement> {
    return this.mutate(`/api/cards/${cardId}/statement/confirm`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  payCard(cardId: string, input: Record<string, unknown>): Promise<unknown> {
    return this.mutate(`/api/cards/${cardId}/payments`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  groups(period: string): Promise<Group[]> {
    return this.request(`/api/groups?period=${encodeURIComponent(period)}`);
  }
  createGroup(input: Record<string, unknown>): Promise<Group> {
    return this.mutate('/api/groups', { method: 'POST', body: JSON.stringify(input) });
  }
  saveRule(groupId: string, period: string, input: Record<string, unknown>): Promise<unknown> {
    return this.mutate(`/api/groups/${groupId}/rules/${period}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }
  confirmRule(groupId: string, period: string, input: Record<string, unknown>): Promise<unknown> {
    return this.mutate(`/api/groups/${groupId}/rules/${period}/confirm`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  receivables(groupId: string, period: string): Promise<Receivable[]> {
    return this.request(`/api/groups/${groupId}/receivables?period=${encodeURIComponent(period)}`);
  }
  collect(groupId: string, input: Record<string, unknown>): Promise<unknown> {
    return this.mutate(`/api/groups/${groupId}/collections`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  private mutate<T>(url: string, init: RequestInit): Promise<T> {
    return this.request<T>(url, {
      ...init,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': this.csrfToken },
    });
  }
  private async request<T>(url: string, init: RequestInit = {}): Promise<T> {
    const response = await this.fetcher(url, { ...init, credentials: 'include' });
    if (!response.ok) {
      let message = `No se pudo completar la solicitud (${response.status}).`;
      try {
        const problem = (await response.json()) as { detail?: string; title?: string };
        message = problem.detail || problem.title || message;
      } catch {
        /* Empty or non-JSON response. */
      }
      throw new ApiProblem(response.status, message);
    }
    if (response.status === 204) return undefined as T;
    return response.json() as Promise<T>;
  }
}
