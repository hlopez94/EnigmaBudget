import { ChangeDetectorRef, Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Account, ExchangeRateSnapshot, FinanceOverview, Operation, SavingsAccountBalance, SavingsAllocation, SavingsGoal, SavingsGoalDetails, SavingsMovement, SavingsRateSide } from '../core/api.models';
import { ApiService } from '../core/api.service';
import { FinanceActions, financeToday, RefreshFinanceOverview } from '../finance/finance-actions';
import { ActionDialog } from '../finance/ui/action-dialog';

type SavingsDialog = '' | 'create' | 'edit' | 'allocate' | 'release' | 'apply' | 'rate' | 'liquidity' | 'reverse' | 'close';
type RateDraft = { mode: 'Snapshot' | 'Manual'; snapshotId: string; house: string; side: '' | SavingsRateSide; manualRate: string; manualReason: string };
const manualRatePattern = /^(?:0|[1-9]\d*)(?:\.\d{1,8})?$/;
const scaled = (value: string, scale: number): bigint | null => {
  const match = /^(\d+)(?:\.(\d+))?$/.exec(value.trim());
  if (!match || (match[2]?.length ?? 0) > scale) return null;
  return BigInt(match[1]) * 10n ** BigInt(scale) + BigInt(((match[2] ?? '').padEnd(scale, '0')) || '0');
};
const decimalString = (value: bigint, scale: number): string => {
  const digits = value.toString().padStart(scale + 1, '0');
  return `${digits.slice(0, -scale)}.${digits.slice(-scale)}`;
};
const equivalent = (amount: string, rate: string, source: string, target: string): string | null => {
  const cents = scaled(amount, 2); const rateUnits = scaled(rate, 8);
  if (cents === null || rateUnits === null || rateUnits === 0n || source === target) return null;
  if (source === 'ARS' && target === 'USD') return decimalString((cents * 10n ** 8n + rateUnits / 2n) / rateUnits, 2);
  if (source === 'USD' && target === 'ARS') return decimalString((cents * rateUnits + 50_000_000n) / 100_000_000n, 2);
  return null;
};

@Component({ selector: 'app-savings', standalone: true, imports: [CommonModule, FormsModule, ActionDialog], templateUrl: './savings.html', styleUrl: './savings.css' })
export class FinanceSavings extends FinanceActions implements OnChanges {
  @Input({ required: true }) overview!: FinanceOverview;
  @Input({ required: true }) refreshOverview!: RefreshFinanceOverview;
  @Input() workspaceName = '';
  goals: SavingsGoal[] = [];
  balances: SavingsAccountBalance[] = [];
  rates: ExchangeRateSnapshot[] = [];
  details: SavingsGoalDetails | null = null;
  selectedGoalId = '';
  historyPage = 1;
  dialog: SavingsDialog = '';
  ratesStatus = '';
  rateError = '';
  loading = false;
  lastRequestId = '';
  goalDraft = { name: '', targetCurrency: 'ARS' as 'ARS' | 'USD', targetAmount: '', desiredDate: '', monthlyContribution: '', nextContributionDate: '', state: 'Active' };
  allocationDraft = { accountId: '', amount: '' };
  releaseDraft = { allocationId: '', amount: '' };
  applicationDraft = { allocationId: '', paymentId: '', amount: '' };
  rateDraft: RateDraft = { mode: 'Snapshot', snapshotId: '', house: '', side: '', manualRate: '', manualReason: '' };
  liquidityDraft = { accountId: '', fundsAvailableFrom: '' };
  reverseDraft = { movementId: '', reason: '', confirm: false };
  paymentFollowupId = '';

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) { super(api, changeDetector); }
  override ngOnChanges(changes: SimpleChanges): void {
    super.ngOnChanges(changes);
    if (((changes['workspaceId'] || changes['contextGeneration']) && this.workspaceId) || (changes['overview'] && !changes['overview'].firstChange && this.workspaceId)) void this.load();
  }
  protected override permissionsChanged(): void {
    if (this.workspaceArchived || (!this.allowed('recordFinance') && ['allocate', 'release', 'apply'].includes(this.dialog)) || (!this.allowed('correctHistory') && this.dialog === 'reverse') || (!this.allowed('manageResources') && ['create', 'edit', 'rate', 'liquidity', 'close'].includes(this.dialog))) this.discardDialogDraft();
  }
  protected override resetForContextChange(): void {
    this.goals = []; this.balances = []; this.details = null; this.selectedGoalId = ''; this.historyPage = 1;
    this.dialog = ''; this.loading = false; this.lastRequestId = ''; this.rates = []; this.ratesStatus = ''; this.rateError = ''; this.paymentFollowupId = '';
    this.goalDraft = this.emptyGoal(); this.allocationDraft = { accountId: '', amount: '' }; this.releaseDraft = { allocationId: '', amount: '' };
    this.applicationDraft = { allocationId: '', paymentId: '', amount: '' }; this.rateDraft = { mode: 'Snapshot', snapshotId: '', house: '', side: '', manualRate: '', manualReason: '' };
  }
  private emptyGoal() { return { name: '', targetCurrency: 'ARS' as 'ARS' | 'USD', targetAmount: '', desiredDate: '', monthlyContribution: '', nextContributionDate: '', state: 'Active' }; }
  private discardDialogDraft(): void { this.dialog = ''; this.goalDraft = this.emptyGoal(); this.allocationDraft = { accountId: '', amount: '' }; this.releaseDraft = { allocationId: '', amount: '' }; this.applicationDraft = { allocationId: '', paymentId: '', amount: '' }; this.reverseDraft = { movementId: '', reason: '', confirm: false }; this.paymentFollowupId = ''; }
  get accounts(): Account[] { return (this.overview?.accounts ?? []).filter(x => !x.isArchived); }
  get selectedGoal(): SavingsGoal | undefined { return this.goals.find(x => x.id === this.selectedGoalId); }
  get activeGoals(): SavingsGoal[] { return this.goals.filter(x => x.state === 'Active'); }
  get activeAllocations(): SavingsAllocation[] { return (this.details?.allocations ?? []).filter(x => (scaled(x.reservedAmount, 2) ?? 0n) > 0n); }
  get selectedBalance(): SavingsAccountBalance | undefined { return this.balances.find(x => x.accountId === this.liquidityDraft.accountId); }
  get canManage(): boolean { return this.allowed('manageResources'); }
  get canRecord(): boolean { return this.allowed('recordFinance'); }
  get canCorrect(): boolean { return this.allowed('correctHistory'); }
  override hasPendingDraft(): boolean {
    return !!(this.goalDraft.name.trim() || this.goalDraft.targetAmount.trim() || this.goalDraft.monthlyContribution.trim() || this.allocationDraft.amount.trim() || this.releaseDraft.amount.trim() || this.applicationDraft.amount.trim() || this.rateDraft.manualRate.trim() || this.rateDraft.manualReason.trim() || this.reverseDraft.reason.trim());
  }

  async load(): Promise<void> {
    if (!this.workspaceId) return;
    const context = this.captureContext(); this.loading = true; this.error = '';
    try {
      const [goalsResult, balancesResult, ratesResult] = await Promise.allSettled([this.api.savingsGoals(), this.api.savingsBalances(), this.api.exchangeRates()]);
      if (!this.isCurrentContext(context)) return;
      if (goalsResult.status === 'rejected') throw goalsResult.reason;
      this.goals = goalsResult.value;
      if (balancesResult.status === 'fulfilled') this.balances = balancesResult.value;
      if (ratesResult.status === 'fulfilled') { const rates = ratesResult.value; this.rates = rates.snapshots; this.ratesStatus = rates.stale ? 'Las cotizaciones están desactualizadas.' : rates.status === 'Unavailable' ? 'No hay cotizaciones disponibles.' : 'Cotizaciones actualizadas.'; this.rateError = rates.error ?? ''; }
      else { this.ratesStatus = 'No se pudieron consultar las cotizaciones.'; this.rateError = this.message(ratesResult.reason); }
    } catch (error) { if (this.isCurrentContext(context)) this.showLoadError(error); }
    finally { if (this.isCurrentContext(context)) { this.loading = false; this.changeDetector.markForCheck(); } }
  }
  async loadDetails(page = 1): Promise<void> {
    if (!this.selectedGoalId) return;
    const id = this.selectedGoalId; const context = this.captureContext();
    try { const details = await this.api.savingsGoalDetails(id, page); if (this.isCurrentContext(context) && id === this.selectedGoalId) { this.details = details; this.historyPage = details.historyPage; } }
    catch (error) { if (this.isCurrentContext(context)) this.showLoadError(error); }
  }
  selectGoal(id: string): void { this.selectedGoalId = id; this.details = null; this.historyPage = 1; void this.loadDetails(); }
  openCreate(): void { if (this.canManage) { this.goalDraft = this.emptyGoal(); this.dialog = 'create'; } }
  openEdit(goal: SavingsGoal): void { if (!this.canManage) return; this.selectedGoalId = goal.id; this.goalDraft = { name: goal.name, targetCurrency: goal.targetCurrency, targetAmount: goal.targetAmount, desiredDate: goal.desiredDate ?? '', monthlyContribution: goal.monthlyContribution ?? '', nextContributionDate: goal.nextContributionDate ?? '', state: goal.state }; this.dialog = 'edit'; }
  openAllocate(goalId: string): void { if (!this.canRecord) return; this.selectedGoalId = goalId; this.allocationDraft = { accountId: this.accounts[0]?.id ?? '', amount: '' }; this.dialog = 'allocate'; }
  openRelease(allocation: SavingsAllocation): void { if (!this.canRecord) return; this.releaseDraft = { allocationId: allocation.id, amount: allocation.reservedAmount }; this.dialog = 'release'; }
  openApply(allocation: SavingsAllocation, paymentId = ''): void { if (!this.canRecord) return; this.applicationDraft = { allocationId: allocation.id, paymentId, amount: '' }; this.dialog = 'apply'; }
  beginPaymentReview(paymentId: string): void { this.paymentFollowupId = paymentId; }
  async continueWithPayment(goalId: string): Promise<void> {
    if (!this.canRecord) return;
    const paymentId = this.paymentFollowupId; if (!paymentId) return;
    this.selectedGoalId = goalId;
    await this.loadDetails();
    const allocation = this.details?.allocations[0];
    if (!allocation) { this.error = 'Este objetivo todavía no tiene fondos reservados para aplicar.'; return; }
    this.openApply(allocation, paymentId); this.paymentFollowupId = '';
  }
  async openRate(goal: SavingsGoal): Promise<void> {
    if (!this.canManage) return;
    this.selectedGoalId = goal.id;
    this.rateDraft = { mode: (goal.rateChoice?.mode as RateDraft['mode']) ?? 'Snapshot', snapshotId: goal.rateChoice?.snapshotId ?? '', house: goal.rateChoice?.house ?? '', side: goal.rateChoice?.side ?? '', manualRate: goal.rateChoice?.mode === 'Manual' ? goal.rateChoice.rate : '', manualReason: goal.rateChoice?.manualReason ?? '' };
    this.dialog = 'rate';
    const snapshotId = goal.rateChoice?.mode === 'Snapshot' ? goal.rateChoice.snapshotId : null;
    if (!snapshotId || this.rates.some(rate => rate.id === snapshotId)) return;
    const context = this.captureContext();
    try {
      const snapshot = await this.api.exchangeRateSnapshot(snapshotId);
      if (this.isCurrentContext(context) && this.selectedGoalId === goal.id && !this.rates.some(rate => rate.id === snapshot.id)) this.rates = [...this.rates, snapshot];
    } catch (error) {
      if (this.isCurrentContext(context) && this.selectedGoalId === goal.id) this.rateError = `No se pudo recuperar la cotización guardada: ${this.message(error)}`;
    }
  }
  openLiquidity(account: Account): void { if (!this.canManage) return; this.liquidityDraft = { accountId: account.id, fundsAvailableFrom: this.balances.find(x => x.accountId === account.id)?.fundsAvailableFrom ?? '' }; this.dialog = 'liquidity'; }
  openReverse(movement: SavingsMovement): void { if (!this.canCorrect || movement.isReversed) return; this.reverseDraft = { movementId: movement.id, reason: '', confirm: false }; this.dialog = 'reverse'; }
  closeDialog(): boolean { if (this.saving) return false; this.dialog = ''; return true; }
  currentRequestId(_scope: string): string { return this.lastRequestId; }
  private setRequestId(scope: string, payload: unknown): string { this.lastRequestId = this.requestId(scope, payload); return this.lastRequestId; }
  private done(scope: string, payload: unknown): void { this.releaseRequestId(scope, payload); }
  private async refreshAll(): Promise<void> { await this.load(); await this.refreshOverview(); if (this.selectedGoalId) await this.loadDetails(this.historyPage); }
  private nullable(value: string): string | null { return value.trim() ? this.decimal(value) : null; }
  private async mutate(scope: string, payload: unknown, callback: (id: string) => Promise<unknown>, success: string, after?: () => Promise<void>): Promise<boolean> {
    const id = this.setRequestId(scope, payload);
    const ok = await this.runMutation(() => callback(id), success, after);
    if (ok) this.done(scope, payload);
    return ok;
  }
  async saveGoal(): Promise<void> {
    if (!this.canManage) return;
    const draft = { ...this.goalDraft, targetAmount: this.decimal(this.goalDraft.targetAmount), desiredDate: this.goalDraft.desiredDate || null, monthlyContribution: this.nullable(this.goalDraft.monthlyContribution), nextContributionDate: this.goalDraft.nextContributionDate || null };
    if (this.dialog === 'create') {
      const id = this.requestId('goal-create', draft); const payload = { id, ...draft };
      if (!(await this.runMutation(() => this.api.createSavingsGoal(payload), 'Objetivo creado.', () => this.refreshAll()))) return;
      this.releaseRequestId('goal-create', draft);
    } else {
      const goal = this.selectedGoal; if (!goal) return;
      const payload = { version: goal.version, ...draft };
      if (!(await this.mutate(`goal-update:${goal.id}`, payload, requestId => this.api.updateSavingsGoal(goal.id, { requestId, version: goal.version, ...draft }), 'Objetivo actualizado.', () => this.refreshAll()))) return;
    }
    this.goalDraft = this.emptyGoal(); this.dialog = '';
  }
  async saveAllocation(): Promise<void> {
    if (!this.canRecord || !this.selectedGoal) return;
    const draft = { goalId: this.selectedGoal.id, accountId: this.allocationDraft.accountId, amount: this.decimal(this.allocationDraft.amount) };
    if (await this.mutate(`allocation:${draft.goalId}`, draft, id => this.api.allocateSavings({ id, ...draft }), 'Reserva registrada.', () => this.refreshAll())) this.dialog = '';
  }
  async saveRelease(): Promise<void> {
    if (!this.canRecord) return;
    const draft = { allocationId: this.releaseDraft.allocationId, amount: this.decimal(this.releaseDraft.amount) };
    if (await this.mutate(`release:${draft.allocationId}`, draft, id => this.api.releaseSavings(draft.allocationId, { id, amount: draft.amount }), 'Reserva liberada. No se movió dinero entre cuentas.', () => this.refreshAll())) this.dialog = '';
  }
  availablePayments(): { operation: Operation; payment: Operation['payments'][number] }[] {
    return (this.overview?.operations ?? []).filter(x => x.kind === 'Expense').flatMap(operation => operation.payments.map(payment => ({ operation, payment })));
  }
  async saveApplication(): Promise<void> {
    if (!this.canRecord || !this.applicationDraft.paymentId) return;
    const draft = { allocationId: this.applicationDraft.allocationId, financePaymentId: this.applicationDraft.paymentId, amount: this.decimal(this.applicationDraft.amount) };
    if (await this.mutate('savings-application', draft, id => this.api.applySavings({ id, ...draft }), 'Pago existente aplicado a la reserva; no se registró un segundo pago.', () => this.refreshAll())) this.dialog = '';
  }
  async saveRate(): Promise<void> {
    const goal = this.selectedGoal; if (!this.canManage || !goal) return;
    const snapshot = this.rates.find(x => x.id === this.rateDraft.snapshotId);
    let payload: Record<string, unknown>;
    if (this.rateDraft.mode === 'Manual') {
      const rate = this.rateDraft.manualRate.trim();
      if (!manualRatePattern.test(rate) || scaled(rate, 8) === null || scaled(rate, 8)! <= 0n || !this.rateDraft.manualReason.trim()) { this.error = 'Ingresá una cotización ARS por USD positiva, con hasta 8 decimales, y explicá el motivo.'; return; }
      payload = { goalVersion: goal.version, mode: 'Manual', snapshotId: null, house: null, side: null, manualArsPerUsd: rate, manualReason: this.rateDraft.manualReason.trim() };
    } else {
      const side = this.rateDraft.side;
      if (!snapshot || (side !== 'Compra' && side !== 'Venta') || (side === 'Compra' ? snapshot.buy : snapshot.sell) === null) { this.error = 'Elegí una cotización de compra o venta disponible.'; return; }
      payload = { goalVersion: goal.version, mode: 'Snapshot', snapshotId: snapshot.id, house: snapshot.house, side, manualArsPerUsd: null, manualReason: null };
    }
    const keyPayload = { goalId: goal.id, ...payload };
    if (await this.mutate(`goal-rate:${goal.id}`, keyPayload, requestId => this.api.setSavingsGoalRate(goal.id, { requestId, ...payload } as never), 'Referencia de cotización actualizada.', () => this.refreshAll())) this.dialog = '';
  }
  async saveLiquidity(): Promise<void> {
    if (!this.canManage) return;
    const account = this.accounts.find(x => x.id === this.liquidityDraft.accountId); if (!account) return;
    const payload = { accountId: account.id, version: account.version, fundsAvailableFrom: this.liquidityDraft.fundsAvailableFrom || null };
    if (await this.mutate(`liquidity:${account.id}`, payload, requestId => this.api.setAccountLiquidity(account.id, { requestId, version: account.version, fundsAvailableFrom: payload.fundsAvailableFrom }), 'Fecha de disponibilidad actualizada.', () => this.refreshAll())) this.dialog = '';
  }
  async reverseApplication(): Promise<void> {
    if (!this.canCorrect || !this.reverseDraft.confirm || !this.reverseDraft.reason.trim()) return;
    const movement = this.details?.movements.find(x => x.id === this.reverseDraft.movementId); if (!movement) return;
    const payload = { movementId: movement.id, reason: this.reverseDraft.reason.trim(), confirm: true };
    if (await this.mutate(`reverse:${movement.id}`, payload, requestId => this.api.reverseSavingsApplication(movement.id, { requestId, reason: payload.reason, confirm: true }), 'Aplicación revertida.', () => this.refreshAll())) this.dialog = '';
  }
  async closeGoal(): Promise<void> {
    const goal = this.selectedGoal; if (!this.canManage || !goal) return;
    const payload = { goalId: goal.id, version: goal.version, confirmRelease: true };
    if (await this.mutate(`goal-close:${goal.id}`, payload, requestId => this.api.closeSavingsGoal(goal.id, { requestId, version: goal.version, confirmRelease: true }), 'Objetivo cerrado y reservas liberadas.', () => this.refreshAll())) this.dialog = '';
  }
  async refreshRates(): Promise<void> {
    if (!this.canManage) return;
    const payload = {};
    const id = this.requestId('rates-refresh', payload); this.lastRequestId = id;
    const context = this.captureContext();
    if (!(await this.runMutation(() => this.api.refreshExchangeRates({ id }), 'Cotizaciones actualizadas.', async () => { const result = await this.api.exchangeRates(); if (this.isCurrentContext(context)) { this.rates = result.snapshots; this.ratesStatus = result.stale ? 'Las cotizaciones están desactualizadas.' : 'Cotizaciones actualizadas.'; this.rateError = result.error ?? ''; } }))) return;
    this.releaseRequestId('rates-refresh', payload);
  }
  rateSource(rate: ExchangeRateSnapshot): string { return `${rate.name} · ${new Date(rate.providerUpdatedAt).toLocaleString('es-AR')}`; }
  equivalent(amount: string, currency: string, goal: SavingsGoal): string | null { return goal.rateChoice ? equivalent(amount, goal.rateChoice.rate, currency, goal.targetCurrency) : null; }
  estimate(goal: SavingsGoal): string { return goal.requiredMonthlyContribution ?? goal.estimateExplanation; }
  accountName(id: string): string { return this.accounts.find(x => x.id === id)?.name ?? 'Cuenta'; }
  get applicationAllocation(): SavingsAllocation | undefined { return this.details?.allocations.find(x => x.id === this.applicationDraft.allocationId); }
  backingLabel(goal: SavingsGoal): string { return goal.backingAmounts.length ? goal.backingAmounts.map(x => `${this.money(x.amount, x.currency)} · ${x.currency}`).join(' + ') : 'Todavía sin fondos reservados'; }
  goalAllocations(goalId: string): SavingsAllocation[] { return this.selectedGoalId === goalId ? (this.details?.allocations ?? []) : []; }
  async history(direction: -1 | 1): Promise<void> { const page = this.historyPage + direction; if (page < 1) return; await this.loadDetails(page); }
  accountBalance(accountId: string): SavingsAccountBalance | undefined { return this.balances.find(x => x.accountId === accountId); }
  accountAvailableDate(accountId: string): string { return this.accountBalance(accountId)?.fundsAvailableFrom ?? ''; }
}
