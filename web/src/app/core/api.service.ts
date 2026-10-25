import { Inject, Injectable, InjectionToken, Optional } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { NativeAuthCoordinator } from './native-auth-coordinator';
import {
  Account,
  AccountSummary,
  Card,
  CardSummary,
  CompleteDraftRequest,
  FinanceOverview,
  FinanceCatalog,
  FinanceIncomeSourceOption,
  Group,
  OnboardingState,
  Operation,
  Purchase,
  Receivable,
  SaveDraftRequest,
  SessionInfo,
  Statement,
  InvitationPreview,
  PersonalSharing,
  Workspace,
  WorkspaceInvitation,
  WorkspaceMember,
  SavingsGoal,
  SavingsGoalCreate,
  SavingsGoalUpdate,
  SavingsGoalDetails,
  SavingsAccountBalance,
  SavingsAllocation,
  SavingsReleaseRequest,
  SavingsRateChoiceInput,
  ExchangeRateList,
  ExchangeRateSnapshot,
  Projection,
  ProjectionCalculateRequest,
  ProjectionScenario,
  ProjectionScenarioDelete,
  ProjectionScenarioSave,
} from './api.models';

export type Fetcher = typeof fetch;
export const API_FETCHER = new InjectionToken<Fetcher>('API_FETCHER');
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
  private readonly fetcher: Fetcher;
  private selectedWorkspaceId: string | null = null;
  private pendingInviteToken: string | null = null;

  constructor(@Optional() @Inject(API_FETCHER) fetcher?: Fetcher, @Optional() private readonly nativeAuth?: NativeAuthCoordinator) {
    this.fetcher = fetcher ?? ((...args) => fetch(...args));
  }

  selectWorkspace(workspaceId: string | null): void {
    this.selectedWorkspaceId = workspaceId;
  }

  clearWorkspace(): void {
    this.selectedWorkspaceId = null;
  }
  get isNativeAndroid(): boolean { return Capacitor.getPlatform() === 'android'; }
  captureNativeInvitation(token: string): void { if (this.isNativeAndroid) this.pendingInviteToken = token; }
  capturePendingInvitation(): string | null {
    if (this.isNativeAndroid) return this.pendingInviteToken;
    const fragment = window.location.hash;
    const match = /^#invite=([^&]+)$/.exec(fragment);
    if (match) {
      this.pendingInviteToken = decodeURIComponent(match[1]);
      try { sessionStorage.setItem('enigma-budget:pending-invite', this.pendingInviteToken); } catch { /* Keep the token only in this tab's memory. */ }
      window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}`);
    } else {
      try { this.pendingInviteToken = sessionStorage.getItem('enigma-budget:pending-invite'); } catch { /* Storage may be unavailable. */ }
    }
    return this.pendingInviteToken;
  }
  pendingInvitation(): string | null { return this.pendingInviteToken; }
  clearPendingInvitation(): void {
    this.pendingInviteToken = null;
    if (this.isNativeAndroid) return;
    try { sessionStorage.removeItem('enigma-budget:pending-invite'); } catch { /* No persisted value to clear. */ }
  }

  async session(): Promise<SessionInfo> {
    if (this.isNativeAndroid && this.nativeAuth) return this.nativeAuth.restore();
    const session = await this.request<SessionInfo>('/api/auth/session');
    this.csrfToken = session.csrfToken;
    return session;
  }
  async signInDevelopment(key?: string, onIdentityInvalidated: () => void = () => undefined): Promise<SessionInfo> {
    if (this.isNativeAndroid && this.nativeAuth) {
      await this.nativeAuth.signInDevelopment(key, onIdentityInvalidated);
      return this.nativeAuth.restore();
    }
    if (!this.csrfToken) await this.session();
    await this.mutate<void>('/api/auth/development', { method: 'POST', body: JSON.stringify(key ? { key } : {}) });
    return this.session();
  }
  async signInGoogle(onIdentityInvalidated: () => void = () => undefined): Promise<void> {
    if (this.isNativeAndroid && this.nativeAuth) return this.nativeAuth.signInGoogle(onIdentityInvalidated);
    onIdentityInvalidated();
    window.location.assign('/api/auth/google');
  }
  async logout(onLocalClear: () => void = () => undefined): Promise<boolean> {
    if (this.isNativeAndroid && this.nativeAuth) {
      return this.nativeAuth.logout(() => {
        this.csrfToken = '';
        this.clearWorkspace();
        this.clearPendingInvitation();
        onLocalClear();
      });
    }
    await this.mutate<void>('/api/auth/logout', { method: 'POST', body: '{}' });
    this.csrfToken = '';
    this.clearWorkspace();
    this.clearPendingInvitation();
    await this.session();
    onLocalClear();
    return true;
  }
  me(): Promise<{ id: string; displayName: string; email: string | null; emailVerified: boolean; personalWorkspaceId: string }> {
    return this.request('/api/me');
  }
  workspaces(): Promise<Workspace[]> {
    return this.request('/api/workspaces');
  }
  workspace(id: string): Promise<Workspace> {
    return this.request(`/api/workspaces/${id}`);
  }
  createWorkspace(input: { id: string; kind: 'Household'; name: string }): Promise<Workspace> {
    return this.mutate('/api/workspaces', { method: 'POST', body: JSON.stringify(input) });
  }
  updateWorkspace(id: string, input: { version: number; name: string; isArchived: boolean }): Promise<Workspace> {
    return this.mutate(`/api/workspaces/${id}`, { method: 'PUT', body: JSON.stringify(input) });
  }
  members(id: string): Promise<WorkspaceMember[]> {
    return this.request(`/api/workspaces/${id}/members`);
  }
  changeMember(id: string, userId: string, input: { version: number; role: string }): Promise<void> {
    return this.mutate(`/api/workspaces/${id}/members/${userId}`, { method: 'PUT', body: JSON.stringify(input) });
  }
  removeMember(id: string, userId: string, version: number): Promise<void> {
    return this.mutate(`/api/workspaces/${id}/members/${userId}`, { method: 'DELETE', body: JSON.stringify({ version }) });
  }
  leaveWorkspace(id: string, version: number): Promise<void> {
    return this.mutate(`/api/workspaces/${id}/leave`, { method: 'POST', body: JSON.stringify({ version }) });
  }
  transferOwner(id: string, version: number, userId: string): Promise<void> {
    return this.mutate(`/api/workspaces/${id}/owner-transfer`, { method: 'POST', body: JSON.stringify({ version, userId }) });
  }
  invitations(id: string): Promise<WorkspaceInvitation[]> {
    return this.request(`/api/workspaces/${id}/invitations`);
  }
  createInvitation(id: string, input: { id: string; email: string; role: string }): Promise<WorkspaceInvitation> {
    return this.mutate(`/api/workspaces/${id}/invitations`, { method: 'POST', body: JSON.stringify(input) });
  }
  revokeInvitation(id: string, invitationId: string, version: number): Promise<void> {
    return this.mutate(`/api/workspaces/${id}/invitations/${invitationId}`, { method: 'DELETE', body: JSON.stringify({ version }) });
  }
  previewInvitation(token: string): Promise<InvitationPreview> {
    return this.mutate('/api/invitations/preview', { method: 'POST', body: JSON.stringify({ token }) });
  }
  acceptInvitation(token: string): Promise<Workspace> {
    return this.mutate('/api/invitations/accept', { method: 'POST', body: JSON.stringify({ token }) });
  }
  personalSharing(): Promise<PersonalSharing[]> {
    return this.request('/api/personal-sharing');
  }
  updatePersonalSharing(householdId: string, version: number, enabled: boolean): Promise<PersonalSharing> {
    return this.mutate(`/api/personal-sharing/${householdId}`, { method: 'PUT', body: JSON.stringify({ version, enabled }) });
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
    return this.financeRequest('/api/accounts');
  }
  cards(): Promise<CardSummary[]> {
    return this.financeRequest('/api/cards');
  }

  financeOverview(period: string): Promise<FinanceOverview> {
    return this.financeRequest<FinanceOverview & { groups?: Group[]; receivables?: Receivable[] }>(`/api/finance?period=${encodeURIComponent(period)}`).then((overview) => ({
      ...overview,
      groups: overview.groups ?? [],
      receivables: overview.receivables ?? [],
    }));
  }
  operations(period: string, accountId?: string): Promise<Operation[]> {
    const query = new URLSearchParams({ period });
    if (accountId) query.set('accountId', accountId);
    return this.financeRequest(`/api/operations?${query}`);
  }
  createAccount(input: Record<string, unknown>): Promise<Account> {
    return this.financeMutate('/api/accounts', { method: 'POST', body: JSON.stringify(input) });
  }
  updateAccount(id: string, input: Record<string, unknown>): Promise<Account> {
    return this.financeMutate(`/api/accounts/${id}`, { method: 'PUT', body: JSON.stringify(input) });
  }
  adjustAccount(id: string, input: Record<string, unknown>): Promise<Account> {
    return this.financeMutate(`/api/accounts/${id}/adjustments`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  createOperation(input: Record<string, unknown>): Promise<Operation> {
    return this.financeMutate('/api/operations', { method: 'POST', body: JSON.stringify(input) });
  }
  financeCatalog(): Promise<FinanceCatalog> {
    return this.financeRequest('/api/finance/catalog');
  }
  createIncomeSource(input: Record<string, unknown>): Promise<FinanceIncomeSourceOption> {
    return this.financeMutate('/api/finance/catalog/income-sources', { method: 'POST', body: JSON.stringify(input) });
  }
  payOperation(id: string, input: Record<string, unknown>): Promise<Operation> {
    return this.financeMutate(`/api/operations/${id}/payments`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  voidOperation(id: string, input: Record<string, unknown>): Promise<Operation> {
    return this.financeMutate(`/api/operations/${id}/void`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  transfer(input: Record<string, unknown>): Promise<unknown> {
    return this.financeMutate('/api/transfers', { method: 'POST', body: JSON.stringify(input) });
  }
  createCard(input: Record<string, unknown>): Promise<Card> {
    return this.financeMutate('/api/cards', { method: 'POST', body: JSON.stringify(input) });
  }
  updateCard(id: string, input: Record<string, unknown>): Promise<Card> {
    return this.financeMutate(`/api/cards/${id}`, { method: 'PUT', body: JSON.stringify(input) });
  }
  purchases(cardId: string): Promise<Purchase[]> {
    return this.financeRequest(`/api/cards/${cardId}/purchases`);
  }
  createPurchase(input: Record<string, unknown>): Promise<Purchase> {
    return this.financeMutate('/api/purchases', { method: 'POST', body: JSON.stringify(input) });
  }
  statement(cardId: string, period: string, currency: string): Promise<Statement> {
    return this.financeRequest(
      `/api/cards/${cardId}/statement?period=${encodeURIComponent(period)}&currency=${currency}`,
    );
  }
  saveStatement(cardId: string, input: Record<string, unknown>): Promise<Statement> {
    return this.financeMutate(`/api/cards/${cardId}/statement`, {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }
  confirmStatement(cardId: string, input: Record<string, unknown>): Promise<Statement> {
    return this.financeMutate(`/api/cards/${cardId}/statement/confirm`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  payCard(cardId: string, input: Record<string, unknown>): Promise<unknown> {
    return this.financeMutate(`/api/cards/${cardId}/payments`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  groups(period: string): Promise<Group[]> {
    return this.financeRequest(`/api/groups?period=${encodeURIComponent(period)}`);
  }
  createGroup(input: Record<string, unknown>): Promise<Group> {
    return this.financeMutate('/api/groups', { method: 'POST', body: JSON.stringify(input) });
  }
  saveRule(groupId: string, period: string, input: Record<string, unknown>): Promise<unknown> {
    return this.financeMutate(`/api/groups/${groupId}/rules/${period}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  }
  confirmRule(groupId: string, period: string, input: Record<string, unknown>): Promise<unknown> {
    return this.financeMutate(`/api/groups/${groupId}/rules/${period}/confirm`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }
  receivables(groupId: string, period: string): Promise<Receivable[]> {
    return this.financeRequest(`/api/groups/${groupId}/receivables?period=${encodeURIComponent(period)}`);
  }
  collect(groupId: string, input: Record<string, unknown>): Promise<unknown> {
    return this.financeMutate(`/api/groups/${groupId}/collections`, {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  savingsGoals(): Promise<SavingsGoal[]> { return this.financeRequest('/api/savings/goals'); }
  savingsGoalDetails(id: string, historyPage = 1): Promise<SavingsGoalDetails> {
    return this.financeRequest(`/api/savings/goals/${encodeURIComponent(id)}/details?historyPage=${historyPage}`);
  }
  createSavingsGoal(input: SavingsGoalCreate): Promise<SavingsGoal> {
    return this.financeMutate('/api/savings/goals', { method: 'POST', body: JSON.stringify(input) });
  }
  updateSavingsGoal(id: string, input: SavingsGoalUpdate): Promise<SavingsGoal> {
    return this.financeMutate(`/api/savings/goals/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(input) });
  }
  setSavingsGoalRate(id: string, input: SavingsRateChoiceInput): Promise<import('./api.models').GoalRateChoice> {
    return this.financeMutate(`/api/savings/goals/${encodeURIComponent(id)}/rate`, { method: 'PUT', body: JSON.stringify(input) });
  }
  closeSavingsGoal(id: string, input: { requestId: string; version: number; confirmRelease: boolean }): Promise<SavingsGoal> {
    return this.financeMutate(`/api/savings/goals/${encodeURIComponent(id)}/close`, { method: 'POST', body: JSON.stringify(input) });
  }
  savingsBalances(): Promise<SavingsAccountBalance[]> { return this.financeRequest('/api/savings/balances'); }
  setAccountLiquidity(accountId: string, input: { requestId: string; version: number; fundsAvailableFrom: string | null }): Promise<SavingsAccountBalance> {
    return this.financeMutate(`/api/savings/accounts/${encodeURIComponent(accountId)}/liquidity`, { method: 'PUT', body: JSON.stringify(input) });
  }
  allocateSavings(input: { id: string; goalId: string; accountId: string; amount: string }): Promise<SavingsAllocation> {
    return this.financeMutate('/api/savings/allocations', { method: 'POST', body: JSON.stringify(input) });
  }
  releaseSavings(id: string, input: SavingsReleaseRequest): Promise<SavingsAllocation> {
    return this.financeMutate(`/api/savings/allocations/${encodeURIComponent(id)}/release`, { method: 'POST', body: JSON.stringify(input) });
  }
  applySavings(input: { id: string; allocationId: string; financePaymentId: string; amount: string }): Promise<unknown> {
    return this.financeMutate('/api/savings/applications', { method: 'POST', body: JSON.stringify(input) });
  }
  reverseSavingsApplication(movementId: string, input: { requestId: string; reason: string; confirm: boolean }): Promise<unknown> {
    return this.financeMutate(`/api/savings/applications/${encodeURIComponent(movementId)}/reverse`, { method: 'POST', body: JSON.stringify(input) });
  }
  exchangeRates(): Promise<ExchangeRateList> { return this.financeRequest('/api/exchange-rates'); }
  exchangeRateSnapshot(snapshotId: string): Promise<ExchangeRateSnapshot> { return this.financeRequest(`/api/exchange-rates/snapshots/${encodeURIComponent(snapshotId)}`); }
  refreshExchangeRates(input: { id: string }): Promise<ExchangeRateList> {
    return this.financeMutate('/api/exchange-rates/refresh', { method: 'POST', body: JSON.stringify(input) });
  }
  projection(through: string, scenario: string, scenarioId?: string): Promise<Projection> {
    const query = new URLSearchParams({ through, scenario });
    if (scenarioId) query.set('scenarioId', scenarioId);
    return this.financeRequest(`/api/projection?${query}`);
  }
  calculateProjection(input: ProjectionCalculateRequest): Promise<Projection> {
    return this.financeMutate('/api/projection/calculate', { method: 'POST', body: JSON.stringify(input) });
  }
  projectionScenarios(): Promise<ProjectionScenario[]> { return this.financeRequest('/api/projection/scenarios'); }
  saveProjectionScenario(input: ProjectionScenarioSave | { id: string; name: string; assumptions: ProjectionScenarioSave['assumptions'] }): Promise<ProjectionScenario> {
    if ('version' in input) {
      const { id, ...body } = input;
      return this.financeMutate(`/api/projection/scenarios/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(body) });
    }
    return this.financeMutate('/api/projection/scenarios', { method: 'POST', body: JSON.stringify(input) });
  }
  deleteProjectionScenario(id: string, input: ProjectionScenarioDelete): Promise<void> {
    return this.financeMutate(`/api/projection/scenarios/${encodeURIComponent(id)}`, { method: 'DELETE', body: JSON.stringify(input) });
  }

  private mutate<T>(url: string, init: RequestInit): Promise<T> {
    return this.request<T>(url, {
      ...init,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': this.csrfToken },
    });
  }
  private financeMutate<T>(url: string, init: RequestInit): Promise<T> {
    const headers = new Headers(init.headers);
    headers.set('Content-Type', 'application/json');
    headers.set('X-CSRF-TOKEN', this.csrfToken);
    const workspaceIdAtDispatch = this.selectedWorkspaceId;
    if (workspaceIdAtDispatch) headers.set('X-Workspace-Id', workspaceIdAtDispatch);
    return this.request(url, { ...init, headers });
  }
  private financeRequest<T>(url: string): Promise<T> {
    const headers = new Headers();
    const workspaceIdAtDispatch = this.selectedWorkspaceId;
    if (workspaceIdAtDispatch) headers.set('X-Workspace-Id', workspaceIdAtDispatch);
    return this.request(url, { headers });
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
