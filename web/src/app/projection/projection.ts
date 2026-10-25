import { ChangeDetectorRef, Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Currency, CurrencyProjection, ExchangeRateList, ExchangeRateSnapshot, Projection, ProjectionAssumption, ProjectionRow, ProjectionScenario } from '../core/api.models';
import { ApiService } from '../core/api.service';
import { FinanceActions, financeId, financeToday } from '../finance/finance-actions';
import { ActionDialog } from '../finance/ui/action-dialog';

export type ProjectionView = 'projection' | 'exchange';
const addMonths = (date: string, months: number): string => { const [y, m, d] = date.split('-').map(Number); const next = new Date(Date.UTC(y, m - 1 + months, d)); return [next.getUTCFullYear(), String(next.getUTCMonth() + 1).padStart(2, '0'), String(next.getUTCDate()).padStart(2, '0')].join('-'); };
const cents = (value: string): bigint | null => { const match = /^(\d+)(?:\.(\d{1,2}))?$/.exec(value.trim()); return match ? BigInt(match[1]) * 100n + BigInt((match[2] ?? '').padEnd(2, '0') || '0') : null; };

@Component({ selector: 'app-projection', standalone: true, imports: [FormsModule, ActionDialog], templateUrl: './projection.html', styleUrl: './projection.css' })
export class FinanceProjection extends FinanceActions implements OnChanges {
  @Input() view: ProjectionView = 'projection';
  @Input() workspaceName = '';
  data: Projection | null = null;
  rates: ExchangeRateList | null = null;
  scenarios: ProjectionScenario[] = [];
  savedRateSelections: { snapshotId: string; goalName: string }[] = [];
  confirmedData: Projection | null = null;
  selectedScenarioId = '';
  selectedRateId = '';
  through = addMonths(financeToday(), 12);
  certainty: 'Confirmed' | 'Estimated' = 'Confirmed';
  selectedDate = financeToday();
  detailFilter: 'date' | 'all' = 'date';
  assumptions: ProjectionAssumption[] = [];
  assumptionDraft: { kind: ProjectionAssumption['kind']; sourceKey: string; date: string; currency: 'ARS' | 'USD'; amount: string; description: string } = this.emptyAssumption();
  scenarioName = '';
  dialog: '' | 'assumption' | 'scenario' | 'deleteScenario' = '';
  loading = false;
  lastRequestId = '';
  private rateRequestId = '';

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) { super(api, changeDetector); }
  override ngOnChanges(changes: SimpleChanges): void { super.ngOnChanges(changes); if ((changes['workspaceId'] || changes['contextGeneration'] || changes['view']) && this.workspaceId) { this.resetForContextChange(); void this.load(); } }
  protected override resetForContextChange(): void {
    this.data = null; this.rates = null; this.scenarios = []; this.savedRateSelections = []; this.confirmedData = null; this.selectedScenarioId = ''; this.selectedRateId = '';
    this.through = addMonths(financeToday(), 12); this.certainty = 'Confirmed'; this.selectedDate = financeToday(); this.detailFilter = 'date'; this.assumptions = [];
    this.assumptionDraft = this.emptyAssumption(); this.scenarioName = ''; this.dialog = ''; this.loading = false; this.lastRequestId = '';
  }
  private emptyAssumption() { return { kind: 'Income' as ProjectionAssumption['kind'], sourceKey: '', date: financeToday(), currency: 'ARS' as 'ARS' | 'USD', amount: '', description: '' }; }
  get canSave(): boolean { return this.allowed('recordFinance'); }
  override hasPendingDraft(): boolean { return this.assumptions.length > 0 || !!this.assumptionDraft.amount.trim() || !!this.assumptionDraft.description.trim() || !!this.scenarioName.trim(); }
  get rows(): ProjectionRow[] { return this.data?.rows ?? []; }
  get currencies(): CurrencyProjection[] { return this.data?.currencies ?? []; }
  get adjustmentSources(): ProjectionRow[] { return this.rows.filter(x => x.direction !== 'Information' && x.direction !== 'Availability' && !['Income', 'Expense'].includes(x.sourceKind)); }
  get selectedScenario(): ProjectionScenario | undefined { return this.scenarios.find(x => x.id === this.selectedScenarioId); }
  get latestRates(): ExchangeRateSnapshot[] {
    const values = this.rates?.snapshots ?? []; const latest = new Map<string, ExchangeRateSnapshot>();
    for (const rate of values) { const current = latest.get(rate.house); if (!current || rate.providerUpdatedAt > current.providerUpdatedAt || (rate.providerUpdatedAt === current.providerUpdatedAt && rate.fetchedAt > current.fetchedAt)) latest.set(rate.house, rate); }
    for (const selected of this.savedRateSelections) { const rate = values.find(x => x.id === selected.snapshotId); if (rate) latest.set(`goal:${rate.id}`, rate); }
    const chosen = values.find(x => x.id === this.selectedRateId); if (chosen) latest.set(`selected:${chosen.id}`, chosen);
    return [...latest.values()].sort((a, b) => a.house.localeCompare(b.house));
  }
  get selectedRate(): ExchangeRateSnapshot | undefined { return (this.rates?.snapshots ?? []).find(x => x.id === this.selectedRateId); }
  async load(): Promise<void> {
    const context = this.captureContext(); if (!this.workspaceId) return; this.loading = true; this.error = '';
    try {
      if (this.view === 'exchange') {
        const [rates, goals] = await Promise.all([this.api.exchangeRates(), this.api.savingsGoals()]);
        if (!this.isCurrentContext(context)) return;
        this.rates = rates; this.savedRateSelections = goals.flatMap(goal => goal.rateChoice?.snapshotId ? [{ snapshotId: goal.rateChoice.snapshotId, goalName: goal.name }] : []);
      }
      else { const [confirmed, scenarios] = await Promise.all([this.api.projection(this.through, 'Confirmed'), this.api.projectionScenarios()]); if (!this.isCurrentContext(context)) return; this.confirmedData = confirmed; this.scenarios = scenarios; this.data = confirmed; this.selectedDate = confirmed.asOf; }
    } catch (error) { if (this.isCurrentContext(context)) this.showLoadError(error); }
    finally { if (this.isCurrentContext(context)) { this.loading = false; this.changeDetector.markForCheck(); } }
  }
  async loadRates(): Promise<void> { const context = this.captureContext(); const rates = await this.api.exchangeRates(); if (this.isCurrentContext(context)) this.rates = rates; }
  async refresh(): Promise<void> { if (this.view === 'exchange') return this.load(); await this.loadProjection(); }
  async loadProjection(): Promise<void> {
    if (!this.workspaceId) return; const context = this.captureContext(); this.loading = true; this.error = '';
    try {
      if (this.certainty === 'Confirmed') {
        const result = await this.api.projection(this.through, 'Confirmed'); if (!this.isCurrentContext(context)) return; this.data = result; this.confirmedData = result;
      } else {
        const result = await this.api.calculateProjection({ through: this.through, scenario: 'Estimated', savedScenarioId: null, assumptions: this.assumptions });
        if (!this.isCurrentContext(context)) return; this.data = result;
      }
      this.selectedDate = this.clampedDate(this.selectedDate); this.loadSelectedDay();
    } catch (error) { if (this.isCurrentContext(context)) this.showLoadError(error); }
    finally { if (this.isCurrentContext(context)) { this.loading = false; this.changeDetector.markForCheck(); } }
  }
  async changeCertainty(value: 'Confirmed' | 'Estimated'): Promise<void> { if (this.saving) return; this.certainty = value; await this.loadProjection(); }
  async changeThrough(): Promise<void> { if (this.saving) return; await this.loadProjection(); }
  async chooseScenario(id: string): Promise<void> {
    if (this.saving) return;
    this.selectedScenarioId = id; const scenario = this.scenarios.find(x => x.id === id); this.assumptions = scenario ? scenario.assumptions.map(x => ({ ...x })) : [];
    this.certainty = 'Estimated'; await this.loadProjection();
  }
  async addAssumption(): Promise<void> {
    if (!this.assumptionDraft.description.trim()) { this.error = 'Describí el supuesto para identificarlo en el resultado.'; return; }
    const draft = { ...this.assumptionDraft }; let assumption: ProjectionAssumption;
    if (draft.kind === 'Income' || draft.kind === 'Expense') {
      const amount = this.normalizeMoney(draft.amount); if (!amount || cents(amount) === null || cents(amount)! <= 0n) { this.error = 'Ingresá un importe positivo con hasta dos decimales.'; return; }
      assumption = { id: financeId(), kind: draft.kind, date: draft.date, currency: draft.currency, amount, description: draft.description.trim(), sourceId: null, sourceKind: null, componentId: null };
    } else {
      const row = this.adjustmentSources.find(x => this.rowKey(x) === draft.sourceKey); if (!row) { this.error = 'Elegí un movimiento visible para ajustar.'; return; }
      if (draft.kind === 'AmountChange') {
        const amount = this.signedMoney(draft.amount); if (!amount || amount === '0.00') { this.error = 'Ingresá una variación distinta de cero con hasta dos decimales.'; return; }
        assumption = { id: financeId(), kind: 'AmountChange', date: null, currency: row.currency, amount, description: draft.description.trim(), sourceId: row.sourceId, sourceKind: row.sourceKind, componentId: row.componentId };
      } else assumption = { id: financeId(), kind: 'DateChange', date: draft.date, currency: null, amount: null, description: draft.description.trim(), sourceId: row.sourceId, sourceKind: row.sourceKind, componentId: row.componentId };
    }
    this.assumptions = [...this.assumptions, assumption]; this.assumptionDraft = this.emptyAssumption(); this.dialog = '';
    if (this.certainty !== 'Estimated') this.certainty = 'Estimated'; await this.loadProjection();
  }
  private normalizeMoney(input: string): string { return input.trim().replace(',', '.'); }
  private signedMoney(input: string): string | null { const value = input.trim().replace(',', '.'); if (!/^-?\d+(?:\.\d{1,2})?$/.test(value)) return null; const parsed = cents(value.startsWith('-') ? value.slice(1) : value); return parsed === null ? null : `${value.startsWith('-') ? '-' : ''}${(parsed / 100n).toString()}.${(parsed % 100n).toString().padStart(2, '0')}`; }
  removeAssumption(id: string): void { if (this.saving) return; this.assumptions = this.assumptions.filter(x => x.id !== id); void this.loadProjection(); }
  rowKey(row: ProjectionRow): string { return `${row.sourceKind}|${row.sourceId}|${row.componentId ?? ''}|${row.currency}`; }
  async saveScenario(): Promise<void> {
    if (!this.canSave || !this.scenarioName.trim()) return;
    const scenario = this.selectedScenario;
    const createKey = { name: this.scenarioName.trim(), assumptions: this.assumptions };
    const updateKey = { version: scenario?.version, name: this.scenarioName.trim(), assumptions: this.assumptions };
    const requestId = scenario ? this.requestId(`scenario-update:${scenario.id}`, updateKey) : this.requestId('scenario-create', createKey);
    const payload = scenario ? { id: scenario.id, requestId, version: scenario.version, name: this.scenarioName.trim(), assumptions: this.assumptions } : { id: requestId, name: this.scenarioName.trim(), assumptions: this.assumptions };
    this.lastRequestId = requestId;
    let savedId = '';
    if (!(await this.runMutation(async () => { const result = await this.api.saveProjectionScenario(payload as never); savedId = result.id; return result; }, 'Escenario guardado.'))) return;
    if (scenario) this.releaseRequestId(`scenario-update:${scenario.id}`, updateKey);
    else this.releaseRequestId('scenario-create', createKey);
    this.scenarioName = ''; this.dialog = ''; await this.load();
    if (savedId) { this.selectedScenarioId = savedId; const saved = this.scenarios.find(x => x.id === savedId); if (saved) this.assumptions = saved.assumptions.map(x => ({ ...x })); }
  }
  openSaveScenario(): void { if (!this.canSave || this.saving) return; this.scenarioName = this.selectedScenario?.name ?? ''; this.dialog = 'scenario'; }
  openDeleteScenario(): void { if (this.canSave && !this.saving && this.selectedScenario) this.dialog = 'deleteScenario'; }
  async deleteScenario(): Promise<void> {
    const scenario = this.selectedScenario; if (!this.canSave || !scenario) return;
    const payload = { version: scenario.version }; const requestId = this.requestId(`scenario-delete:${scenario.id}`, payload); this.lastRequestId = requestId;
    if (!(await this.runMutation(() => this.api.deleteProjectionScenario(scenario.id, { requestId, version: scenario.version }), 'Escenario eliminado.'))) return;
    this.releaseRequestId(`scenario-delete:${scenario.id}`, payload); this.selectedScenarioId = ''; this.assumptions = []; this.dialog = ''; await this.loadProjection(); await this.loadScenarios();
  }
  private async loadScenarios(): Promise<void> { try { this.scenarios = await this.api.projectionScenarios(); } catch (error) { this.showLoadError(error); } }
  async refreshRates(): Promise<void> {
    if (!this.allowed('manageResources')) return;
    if (!this.rateRequestId) this.rateRequestId = financeId();
    const context = this.captureContext();
    await this.runMutation(() => this.api.refreshExchangeRates({ id: this.rateRequestId }), 'Cotizaciones actualizadas.', async () => { const rates = await this.api.exchangeRates(); if (this.isCurrentContext(context)) this.rates = rates; });
  }
  dateBalance(currency: CurrencyProjection): string { return currency.dailyBalances.find(x => x.date === this.selectedDate)?.freeBalance ?? currency.freeBalance; }
  dateCash(currency: CurrencyProjection): string { return currency.dailyBalances.find(x => x.date === this.selectedDate)?.cashBalance ?? currency.cashBalance; }
  firstShortfallSources(currency: CurrencyProjection): ProjectionRow[] { return currency.firstShortfallDate ? this.rows.filter(x => x.date === currency.firstShortfallDate && x.currency === currency.currency) : []; }
  filteredRows(): ProjectionRow[] { if (this.detailFilter === 'all') return this.rows; return this.rows.filter(x => x.date === this.selectedDate || x.date === null || x.dateCertainty === 'Overdue' || x.warnings.some(w => /atrasad/i.test(w))); }
  rowDateLabel(row: ProjectionRow): string { return row.date ? `${row.date}${row.dateCertainty === 'Overdue' ? ' · vencido' : ''}` : 'Sin fecha confiable'; }
  includedReason(row: ProjectionRow): string { if (row.direction === 'Information') return 'Dato informativo: no suma efectivo.'; if (row.dateCertainty === 'Overdue') return 'Vencimiento atrasado; se evalúa desde hoy.'; if (!row.date) return 'Sin fecha confiable: el compromiso se muestra y puede hacer incompleta la proyección.'; return row.certainty === 'Estimated' ? 'Compromiso estimado incluido en el escenario.' : 'Compromiso confirmado incluido en la serie.'; }
  sourceLabel(row: ProjectionRow): string { return ({ FinanceOperation: 'Gasto pendiente', CardObligation: 'Obligación de tarjeta', CardCredit: 'Crédito informativo de tarjeta', GroupReceivable: 'Cobro compartido estimado', LedgerEntry: 'Movimiento confirmado', AccountLiquidity: 'Disponibilidad de cuenta', AccountOpening: 'Saldo inicial', Income: 'Ingreso del escenario', Expense: 'Gasto del escenario', AmountChange: 'Ajuste de importe', DateChange: 'Cambio de fecha' } as Record<string, string>)[row.sourceKind] ?? 'Movimiento proyectado'; }
  assumptionSource(assumption: ProjectionAssumption): string {
    const row = this.rows.find(x => x.sourceId === assumption.sourceId && x.sourceKind === assumption.sourceKind && (assumption.componentId == null || x.componentId === assumption.componentId));
    return row ? `${row.description} · ${row.date ?? 'sin fecha'} · ${row.currency} ${row.amount}` : 'Origen no visible en el período actual';
  }
  currencyLabel(code: string): string { return code === 'ARS' ? 'Pesos argentinos · ARS' : 'Dólares estadounidenses · USD'; }
  certaintyLabel(value: string): string { return value === 'Estimated' ? 'Estimado' : value === 'Confirmed' ? 'Confirmado' : value; }
  changeDay(direction: -1 | 1): void { const date = new Date(`${this.selectedDate}T00:00:00Z`); date.setUTCDate(date.getUTCDate() + direction); const next = date.toISOString().slice(0, 10); this.selectedDate = this.clampedDate(next); }
  private clampedDate(date: string): string { const start = this.data?.asOf ?? financeToday(); return date < start ? start : date > this.through ? this.through : date; }
  loadSelectedDay(): void { this.selectedDate = this.clampedDate(this.selectedDate); }
  shortfallExplanation(currency: CurrencyProjection): string { return currency.firstShortfallDate ? `El efectivo libre mínimo es ${this.money(currency.minimumFreeBalance, currency.currency)} el ${this.dateLabel(currency.firstShortfallDate)}. Revisá los compromisos de esa misma fecha en el detalle.` : `El mínimo de efectivo libre es ${this.money(currency.minimumFreeBalance, currency.currency)}; no aparece un primer faltante en el horizonte consultado.`; }
  dateLabel(date: string | null): string { return date ? new Date(`${date}T12:00:00`).toLocaleDateString('es-AR', { dateStyle: 'medium' }) : 'Sin fecha confiable'; }
  rateChoice(rate: ExchangeRateSnapshot): string { return `${rate.name} · proveedor ${rate.providerUpdatedAt} · consulta ${rate.fetchedAt}`; }
  rateSource(rate: ExchangeRateSnapshot): string { return `${rate.house} · ${rate.name} · proveedor ${rate.providerUpdatedAt} · consulta ${rate.fetchedAt}`; }
  savedRateGoals(rate: ExchangeRateSnapshot): string { return this.savedRateSelections.filter(x => x.snapshotId === rate.id).map(x => x.goalName).join(', '); }
  rateState(): string { return this.rates?.stale ? 'Cotizaciones desactualizadas' : this.rates?.status === 'Disponible' ? 'Cotizaciones actualizadas' : this.rates?.status ?? 'Cargando estado'; }
  closeDialog(): boolean { if (this.saving) return false; this.dialog = ''; return true; }
}
