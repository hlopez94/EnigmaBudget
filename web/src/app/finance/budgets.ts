import { ChangeDetectionStrategy, ChangeDetectorRef, Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FinanceCatalog, FinanceCategoryOption, MonthlyBudgetCreate, MonthlyBudgetRow, MonthlyBudgetSource, MonthlyBudgetUpdate } from '../core/api.models';
import { ApiProblem, ApiService } from '../core/api.service';
import { formatMinorUnits, formatMoney, toMinorUnits, validAmount } from '../onboarding/draft';
import { ActionDialog } from './ui/action-dialog';
import { FinanceActions } from './finance-actions';

type SourceDetails = { identity: string; label: string; category: string; dateLabel: string; date: string; amount: string; currency: 'ARS' | 'USD'; kindLabel: string };
@Component({ selector: 'app-budgets', standalone: true, imports: [FormsModule, ActionDialog], templateUrl: './budgets.html', styleUrl: './budgets.css', changeDetection: ChangeDetectionStrategy.OnPush })
export class FinanceBudgets extends FinanceActions implements OnChanges {
  @Input() period = '';
  rows: MonthlyBudgetRow[] = [];
  catalog: FinanceCatalog = { categories: [], merchants: [], incomeSources: [] };
  currency: 'ARS' | 'USD' = 'ARS';
  loading = false;
  selectedBudget: MonthlyBudgetRow | null = null;
  selectedSource: SourceDetails | null = null;
  editorOpen = false;
  editing = false;
  categoryId = '';
  limitDraft = '';
  private queryGeneration = 0;
  private epoch = 0;

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) { super(api, changeDetector); }
  override ngOnChanges(changes: SimpleChanges): void {
    super.ngOnChanges(changes);
    if (changes['workspaceId']) { this.epoch++; this.clearContext(); }
    if (changes['period']) { this.epoch++; this.clearDraftsAndResults(); }
    if ((changes['workspaceId'] || changes['contextGeneration'] || changes['period']) && this.workspaceId && this.period && this.permissions?.read) void this.load();
  }
  override hasPendingDraft(): boolean { return this.editorOpen && this.limitDraft !== ''; }
  protected override resetForContextChange(): void { this.epoch++; this.clearContext(); }
  private clearContext(): void { this.clearDraftsAndResults(); this.catalog = { categories: [], merchants: [], incomeSources: [] }; }
  private clearDraftsAndResults(): void { this.queryGeneration++; this.rows = []; this.closeDialogs(); this.error = ''; this.notice = ''; }
  private closeDialogs(): void { this.selectedBudget = null; this.selectedSource = null; this.editorOpen = false; this.editing = false; this.categoryId = ''; this.limitDraft = ''; }
  get canManage(): boolean { return !!this.permissions?.manageResources && !this.workspaceArchived; }
  get activeExpenseCategories(): FinanceCategoryOption[] { return this.catalog.categories.filter(item => item.kind === 'Expense' && !item.isArchived); }
  get isLimitValid(): boolean { return validAmount(this.limitDraft, true) && this.limitDraft.trim() !== ''; }
  hasStaleNotice(): boolean { return /cambi[oó]|actualizá/i.test(this.notice); }
  hasStaleError(): boolean { return this.lastFailure instanceof ApiProblem && this.lastFailure.status === 409 || /versi[oó]n|cambi[oó]|conflicto/i.test(this.error); }
  async setCurrency(currency: 'ARS' | 'USD'): Promise<void> {
    if (currency === this.currency) return;
    this.currency = currency;
    this.rows = [];
    this.selectedBudget = null;
    this.selectedSource = null;
    this.error = '';
    this.notice = '';
    await this.load();
  }
  async load(): Promise<void> {
    const ws = this.workspaceId; const period = this.period; const currency = this.currency;
    if (!ws || !period || !this.permissions?.read) { this.rows = []; return; }
    const token = ++this.queryGeneration; const epoch = this.epoch; const context = this.captureContext(); this.loading = true;
    try {
      const [rows, catalog] = await Promise.all([this.api.budgets(period, currency), this.api.financeCatalog()]);
      if (token === this.queryGeneration && epoch === this.epoch && this.isCurrentContext(context) && ws === this.workspaceId && period === this.period && currency === this.currency) {
        this.rows = rows; this.catalog = catalog;
      }
    } catch (error) {
      if (token === this.queryGeneration && epoch === this.epoch && this.isCurrentContext(context) && ws === this.workspaceId && period === this.period && currency === this.currency) this.showLoadError(error);
    } finally { if (token === this.queryGeneration && epoch === this.epoch && this.isCurrentContext(context)) { this.loading = false; this.changeDetector.markForCheck(); } }
  }
  override money(value: string, currency: 'ARS' | 'USD'): string { return formatMoney(value, currency); }
  sourceIdentity(source: MonthlyBudgetSource): string { return `${source.sourceKind}:${source.sourceId}:${source.componentId ?? ''}`; }
  sourceKindLabel(kind: MonthlyBudgetSource['sourceKind']): string { return kind === 'Operation' ? 'Operación' : kind === 'Installment' ? 'Cuota de tarjeta' : 'Ajuste de resumen'; }
  sourceDateLabel(kind: MonthlyBudgetSource['sourceKind']): string { return kind === 'Operation' ? 'Fecha de operación' : kind === 'Installment' ? 'Fecha de compra' : 'Período del resumen'; }
  consumptionAmount(row: MonthlyBudgetRow): string { return formatMinorUnits((toMinorUnits(row.spentAmount) ?? 0n) + (toMinorUnits(row.committedCardAmount) ?? 0n)); }
  showsRemainingCap(row: MonthlyBudgetRow): boolean { return row.isConfigured && row.remainingAmount !== null; }
  isOverBudget(row: MonthlyBudgetRow): boolean { return row.remainingAmount !== null && (toMinorUnits(row.remainingAmount) ?? 0n) < 0n; }
  limitLabel(row: MonthlyBudgetRow): string { return row.isConfigured && row.limitAmount !== null ? this.money(row.limitAmount, row.currency) : 'Sin presupuestar'; }
  get availableRows(): MonthlyBudgetRow[] { return this.rows; }
  openCreate(): void { if (!this.canManage) return; this.selectedBudget = null; this.editing = false; this.editorOpen = true; this.categoryId = this.activeExpenseCategories[0]?.itemId ?? ''; this.limitDraft = ''; }
  openLimit(row: MonthlyBudgetRow): void { if (!this.canManage || !row.isConfigured || !row.budgetId || row.version === null) return; this.selectedBudget = row; this.editing = true; this.editorOpen = true; this.categoryId = row.categoryId; this.limitDraft = row.limitAmount ?? ''; }
  closeEditor(): void { if (!this.saving) this.editorOpen = false; }
  async saveLimit(): Promise<void> {
    if (!this.canManage || !this.isLimitValid) return;
    const limitAmount = this.limitDraft.trim();
    if (this.editing) {
      const row = this.selectedBudget; if (!row?.budgetId || row.version === null) return;
      const payload = { version: row.version, limitAmount };
      const request: MonthlyBudgetUpdate = { requestId: this.requestId(`budget-update:${row.budgetId}`, payload), ...payload };
      if (!(await this.runMutation(() => this.api.updateBudget(row.budgetId!, request), 'Límite actualizado.', () => this.load()))) {
        if (this.isStale(this.error)) this.notice = 'El presupuesto cambió. Conservamos tu borrador; actualizá los datos para revisar antes de guardar.';
        return;
      }
      this.releaseRequestId(`budget-update:${row.budgetId}`, payload); this.editorOpen = false; return;
    }
    const row = this.activeExpenseCategories.find(item => item.itemId === this.categoryId); if (!row) return;
    const payload: Omit<MonthlyBudgetCreate, 'id'> = { period: this.period, categoryId: row.itemId, currency: this.currency, limitAmount };
    const request: MonthlyBudgetCreate = { id: this.requestId('budget-create', payload), ...payload };
    if (!(await this.runMutation(() => this.api.createBudget(request), 'Presupuesto guardado.', () => this.load()))) return;
    this.releaseRequestId('budget-create', payload); this.editorOpen = false;
  }
  async reloadStale(): Promise<void> {
    const staleBudgetId = this.selectedBudget?.budgetId;
    await this.load();
    if (staleBudgetId) this.selectedBudget = this.rows.find(row => row.budgetId === staleBudgetId) ?? this.selectedBudget;
    this.error = '';
    this.notice = 'Datos actualizados. Revisá el límite y guardá nuevamente.';
  }
  openSource(row: MonthlyBudgetRow, source: MonthlyBudgetSource): void {
    const kindLabel = this.sourceKindLabel(source.sourceKind);
    const dateLabel = source.sourceKind === 'Operation' ? 'Fecha de operación' : source.sourceKind === 'Installment' ? 'Fecha de compra' : 'Período del resumen';
    this.selectedSource = { identity: `${source.sourceKind}:${source.sourceId}:${source.componentId ?? ''}`, label: source.description, category: source.categorySnapshot || row.categoryName, dateLabel, date: this.dateLabel(source.date), amount: source.amount, currency: row.currency, kindLabel };
  }
  closeSource(): void { this.selectedSource = null; }
  dateLabel(value: string | null): string { if (!value) return 'Sin fecha informada'; const [year, month, day] = value.slice(0, 10).split('-'); return year && month && day ? `${day}/${month}/${year}` : value; }
  private isStale(message: string): boolean { return this.lastFailure instanceof ApiProblem && this.lastFailure.status === 409 || /versi[oó]n|actualiz|conflicto/i.test(message); }
}
