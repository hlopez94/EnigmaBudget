import { ChangeDetectorRef, Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FinanceCatalog, FinanceOverview, Operation, FinanceCategoryOption, FinanceMerchantOption, FinanceIncomeSourceOption } from '../core/api.models';
import { ApiService } from '../core/api.service';
import { toMinorUnits } from '../onboarding/draft';
import { FinanceActions, financeToday, RefreshFinanceOverview } from './finance-actions';
import { ActionDialog } from './ui/action-dialog';

@Component({
  selector: 'app-operations',
  standalone: true,
  imports: [FormsModule, ActionDialog],
  templateUrl: './operations.html',
  styleUrl: './finance-shell.css',
})
export class FinanceOperations extends FinanceActions {
  activeForm: '' | 'operation' | 'transfer' = '';
  @Input({ required: true }) overview!: FinanceOverview;
  @Input() period = '';
  @Input({ required: true }) refreshOverview!: RefreshFinanceOverview;
  historyFilterAccount = '';
  catalog: FinanceCatalog = { categories: [], merchants: [], incomeSources: [] };
  sourceCreateName = '';
  sourceCreateKind: FinanceIncomeSourceOption['kind'] = 'Salary';
  operationDraft = {
    kind: 'Expense' as 'Income' | 'Expense',
    accountId: '',
    date: financeToday(),
    description: '',
    category: '',
    categoryId: '',
    merchantId: '',
    incomeSourceId: '',
    amount: '',
    paid: true,
  };
  payment = { operationId: '', accountId: '', amount: '', date: financeToday() };
  transferDraft = {
    fromAccountId: '',
    toAccountId: '',
    date: financeToday(),
    amount: '',
    destinationAmount: '',
  };

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) {
    super(api, changeDetector);
  }
  protected override permissionsChanged(): void {
    if (this.workspaceArchived || !this.allowed('recordFinance')) this.activeForm = '';
  }
  protected override resetForContextChange(): void {
    this.activeForm = '';
    this.historyFilterAccount = '';
    this.operationDraft = { kind: 'Expense', accountId: '', date: financeToday(), description: '', category: '', categoryId: '', merchantId: '', incomeSourceId: '', amount: '', paid: true };
    this.catalog = { categories: [], merchants: [], incomeSources: [] };
    this.sourceCreateName = '';
    this.payment = { operationId: '', accountId: '', amount: '', date: financeToday() };
    this.transferDraft = { fromAccountId: '', toAccountId: '', date: financeToday(), amount: '', destinationAmount: '' };
  }
  get accounts() {
    return this.overview?.accounts ?? [];
  }
  get activeAccounts() {
    return this.accounts.filter((account) => !account.isArchived);
  }
  get activeCategories(): FinanceCategoryOption[] {
    return this.catalog.categories.filter((item) => !item.isArchived && item.kind === this.operationDraft.kind);
  }
  get activeMerchants(): FinanceMerchantOption[] { return this.catalog.merchants.filter((item) => !item.isArchived); }
  get activeIncomeSources(): FinanceIncomeSourceOption[] { return this.catalog.incomeSources.filter((item) => !item.isArchived); }
  get transferCurrenciesDiffer(): boolean {
    const source = this.accounts.find((account) => account.id === this.transferDraft.fromAccountId);
    const destination = this.accounts.find(
      (account) => account.id === this.transferDraft.toAccountId,
    );
    return !!source && !!destination && source.currency !== destination.currency;
  }
  get operations() {
    return this.overview?.operations ?? [];
  }
  override hasPendingDraft(): boolean {
    return this.operationDraft.description.trim() !== '' || this.operationDraft.category.trim() !== '' || this.operationDraft.categoryId !== '' || this.operationDraft.merchantId !== '' || this.operationDraft.incomeSourceId !== '' || this.sourceCreateName.trim() !== '' || this.operationDraft.amount.trim() !== '' || this.operationDraft.kind !== 'Expense' || !this.operationDraft.paid || this.payment.amount.trim() !== '' || this.payment.accountId.trim() !== '' || this.transferDraft.fromAccountId !== '' || this.transferDraft.toAccountId !== '' || this.transferDraft.amount.trim() !== '' || this.transferDraft.destinationAmount.trim() !== '';
  }
  openForm(form: 'operation' | 'transfer'): void {
    if (!this.allowed('recordFinance')) return;
    this.activeForm = form;
    if (form === 'operation') void this.loadCatalog();
  }
  changeOperationKind(kind: 'Income' | 'Expense'): void {
    this.operationDraft.kind = kind;
    this.operationDraft.categoryId = '';
    if (kind === 'Expense') this.operationDraft.incomeSourceId = '';
  }
  private async loadCatalog(): Promise<void> {
    if (!this.api.financeCatalog) return;
    const epoch = this.captureContext();
    try {
      const catalog = await this.api.financeCatalog();
      if (this.isCurrentContext(epoch)) this.catalog = catalog;
    } catch (error) { if (this.isCurrentContext(epoch)) this.showLoadError(error); }
  }
  closeForm(): void {
    if (!this.saving) this.activeForm = '';
  }

  filteredOperations(): Operation[] {
    return this.operations.filter(
      (operation) =>
        !this.historyFilterAccount || operation.accountId === this.historyFilterAccount,
    );
  }
  accountName(accountId: string): string {
    return this.accounts.find((account) => account.id === accountId)?.name ?? 'Cuenta';
  }

  async saveOperation(): Promise<void> {
    if (!this.allowed('recordFinance')) return;
    const draft = { ...this.operationDraft };
    const payload = {
      ...draft,
      amount: this.decimal(draft.amount),
      category: draft.categoryId ? null : draft.category || null,
      categoryId: draft.categoryId || null,
      merchantId: draft.merchantId || null,
      incomeSourceId: draft.kind === 'Income' ? draft.incomeSourceId || null : null,
    };
    if (!draft.categoryId || (draft.kind === 'Income' && !draft.incomeSourceId)) {
      this.error = draft.kind === 'Income' ? 'Elegí una categoría y una fuente de ingreso.' : 'Elegí una categoría.';
      this.changeDetector.markForCheck();
      return;
    }
    const request = { id: this.requestId('operation', payload), ...payload };
    if (
      !(await this.runMutation(() => this.api.createOperation(request), 'Movimiento registrado.'))
    )
      return;
    this.releaseRequestId('operation', payload);
    this.operationDraft = {
      kind: 'Expense',
      accountId: this.activeAccounts[0]?.id ?? '',
      date: financeToday(),
      description: '',
      category: '',
      categoryId: '',
      merchantId: '',
      incomeSourceId: '',
      amount: '',
      paid: true,
    };
    this.activeForm = '';
    await this.refreshOverview();
  }

  async createSourceFromMovement(): Promise<void> {
    if (!this.allowed('manageResources')) return;
    const name = this.sourceCreateName.trim();
    if (!name) { this.error = 'Ingresá el nombre de la fuente de ingreso.'; return; }
    const payload = { name, kind: this.sourceCreateKind };
    const request = { id: this.requestId('income-source-create', payload), ...payload };
    const result: { source: FinanceIncomeSourceOption | null } = { source: null };
    if (!(await this.runMutation(async () => { result.source = await this.api.createIncomeSource(request); }, 'Fuente de ingreso creada.'))) return;
    this.releaseRequestId('income-source-create', payload);
    if (result.source) {
      this.catalog = { ...this.catalog, incomeSources: [...this.catalog.incomeSources, result.source] };
      this.operationDraft.incomeSourceId = result.source.incomeSourceId;
      this.sourceCreateName = '';
    }
  }

  async payOperation(operation: Operation): Promise<void> {
    if (!this.allowed('recordFinance')) return;
    const draft = { ...this.payment };
    const payload = {
      accountId: draft.accountId || operation.accountId,
      date: draft.date,
      amount: this.decimal(draft.amount),
    };
    if (
      !(await this.runMutation(
        () =>
          this.api.payOperation(operation.id, {
            id: this.requestId(`operation-payment:${operation.id}`, payload),
            ...payload,
          }),
        'Pago registrado.',
      ))
    )
      return;
    this.releaseRequestId(`operation-payment:${operation.id}`, payload);
    this.payment = { operationId: '', accountId: '', amount: '', date: financeToday() };
    await this.refreshOverview();
  }

  async voidOperation(operation: Operation): Promise<void> {
    if (!this.allowed('correctHistory')) return;
    const reason = window.prompt('Motivo de anulación');
    if (!reason?.trim()) return;
    const payload = { version: operation.version, reason: reason.trim() };
    if (
      !(await this.runMutation(
        () =>
          this.api.voidOperation(operation.id, {
            id: this.requestId(`operation-void:${operation.id}`, payload),
            version: operation.version,
            reason: reason.trim(),
          }),
        'Movimiento anulado.',
      ))
    )
      return;
    this.releaseRequestId(`operation-void:${operation.id}`, payload);
    await this.refreshOverview();
  }

  async transfer(): Promise<void> {
    if (!this.allowed('recordFinance')) return;
    const draft = { ...this.transferDraft };
    const source = this.activeAccounts.find((account) => account.id === draft.fromAccountId);
    const destination = this.activeAccounts.find((account) => account.id === draft.toAccountId);
    if (!source || !destination) {
      this.error = 'Elegí las cuentas de origen y destino.';
      this.changeDetector.markForCheck();
      return;
    }
    const amount = this.decimal(draft.amount);
    const destinationAmountEntered = draft.destinationAmount.trim() !== '';
    if (source.currency !== destination.currency && !destinationAmountEntered) {
      this.error = 'Ingresá el importe real que recibe la cuenta de destino.';
      this.changeDetector.markForCheck();
      return;
    }
    const destinationAmount = destinationAmountEntered
      ? this.decimal(draft.destinationAmount)
      : amount;
    if (
      source.currency === destination.currency &&
      toMinorUnits(destinationAmount) !== toMinorUnits(amount)
    ) {
      this.error = 'Para cuentas en la misma moneda, ambos importes deben coincidir.';
      this.changeDetector.markForCheck();
      return;
    }
    const payload = {
      ...draft,
      amount,
      destinationAmount,
    };
    if (
      !(await this.runMutation(
        () => this.api.transfer({ id: this.requestId('transfer', payload), ...payload }),
        'Transferencia registrada.',
      ))
    )
      return;
    this.releaseRequestId('transfer', payload);
    this.transferDraft = {
      fromAccountId: '',
      toAccountId: '',
      date: financeToday(),
      amount: '',
      destinationAmount: '',
    };
    this.activeForm = '';
    await this.refreshOverview();
  }
}
