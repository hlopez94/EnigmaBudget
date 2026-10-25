import { ChangeDetectorRef, Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Account, FinanceOverview } from '../core/api.models';
import { ApiService } from '../core/api.service';
import { FinanceActions, financeToday, RefreshFinanceOverview } from './finance-actions';
import { ActionDialog } from './ui/action-dialog';

@Component({
  selector: 'app-accounts',
  standalone: true,
  imports: [FormsModule, ActionDialog],
  templateUrl: './accounts.html',
  styleUrl: './finance-shell.css',
})
export class FinanceAccounts extends FinanceActions {
  activeForm: '' | 'create' | 'adjust' | 'edit' = '';
  @Input({ required: true }) overview!: FinanceOverview;
  @Input() period = '';
  @Input({ required: true }) refreshOverview!: RefreshFinanceOverview;
  selectedAccountId = '';
  accountDraft = {
    name: '',
    bankName: '',
    currency: 'ARS' as const,
    openingBalance: '0',
    openingDate: financeToday(),
  };
  accountEdit = { id: '', name: '', bankName: '', isArchived: false, version: 0 };
  accountAdjustment = { date: financeToday(), observedBalance: '', reason: '' };

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) {
    super(api, changeDetector);
  }

  get accounts(): Account[] {
    return this.overview?.accounts ?? [];
  }
  get activeAccounts(): Account[] {
    return this.accounts.filter((account) => !account.isArchived);
  }
  get selectedAccount(): Account | undefined {
    return this.accounts.find((account) => account.id === this.selectedAccountId);
  }

  openForm(form: 'create' | 'adjust' | 'edit'): void {
    this.activeForm = form;
  }
  closeForm(): void {
    if (!this.saving) this.activeForm = '';
  }

  async saveAccount(): Promise<void> {
    const payload = {
      ...this.accountDraft,
      openingBalance: this.decimal(this.accountDraft.openingBalance),
    };
    const request = {
      id: this.requestId('account-create', payload),
      ...payload,
    };
    if (!(await this.runMutation(() => this.api.createAccount(request), 'Cuenta creada.'))) return;
    this.releaseRequestId('account-create', payload);
    this.accountDraft = {
      name: '',
      bankName: '',
      currency: 'ARS',
      openingBalance: '0',
      openingDate: financeToday(),
    };
    this.activeForm = '';
    await this.refreshOverview();
  }

  editAccount(account: Account): void {
    this.accountEdit = {
      id: account.id,
      name: account.name,
      bankName: account.bankName,
      isArchived: account.isArchived,
      version: account.version,
    };
  }

  async saveAccountEdit(): Promise<void> {
    const draft = { ...this.accountEdit };
    if (
      !(await this.runMutation(
        () =>
          this.api.updateAccount(draft.id, {
            version: draft.version,
            name: draft.name,
            bankName: draft.bankName,
            isArchived: draft.isArchived,
          }),
        'Cuenta actualizada.',
      ))
    )
      return;
    this.accountEdit = { id: '', name: '', bankName: '', isArchived: false, version: 0 };
    this.activeForm = '';
    await this.refreshOverview();
  }

  async adjustAccount(): Promise<void> {
    const account = this.selectedAccount;
    if (!account) return;
    const payload = {
      ...this.accountAdjustment,
      observedBalance: this.decimal(this.accountAdjustment.observedBalance),
    };
    const request = {
      ...payload,
      id: this.requestId(`account-adjustment:${account.id}`, payload),
    };
    if (
      !(await this.runMutation(
        () => this.api.adjustAccount(account.id, request),
        'Saldo conciliado.',
      ))
    )
      return;
    this.releaseRequestId(`account-adjustment:${account.id}`, payload);
    this.accountAdjustment = { date: financeToday(), observedBalance: '', reason: '' };
    this.activeForm = '';
    await this.refreshOverview();
  }
}
