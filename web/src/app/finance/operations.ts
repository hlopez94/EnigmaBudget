import { ChangeDetectorRef, Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FinanceOverview, Operation } from '../core/api.models';
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
  operationDraft = {
    kind: 'Expense' as 'Income' | 'Expense',
    accountId: '',
    date: financeToday(),
    description: '',
    category: '',
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
  get accounts() {
    return this.overview?.accounts ?? [];
  }
  get activeAccounts() {
    return this.accounts.filter((account) => !account.isArchived);
  }
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
    return this.operationDraft.description.trim() !== '' || this.operationDraft.category.trim() !== '' || this.operationDraft.amount.trim() !== '' || this.operationDraft.accountId !== '' || this.operationDraft.kind !== 'Expense' || !this.operationDraft.paid || this.payment.amount.trim() !== '' || this.payment.accountId.trim() !== '' || this.transferDraft.fromAccountId !== '' || this.transferDraft.toAccountId !== '' || this.transferDraft.amount.trim() !== '' || this.transferDraft.destinationAmount.trim() !== '';
  }
  openForm(form: 'operation' | 'transfer'): void {
    if (!this.allowed('recordFinance')) return;
    this.activeForm = form;
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
      category: draft.category || null,
    };
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
      amount: '',
      paid: true,
    };
    this.activeForm = '';
    await this.refreshOverview();
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
