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
  private accountEditOriginal: typeof this.accountEdit | null = null;
  accountAdjustment = { date: financeToday(), observedBalance: '', reason: '' };

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) {
    super(api, changeDetector);
  }
  protected override permissionsChanged(): void {
    if (this.workspaceArchived || !this.allowed('manageResources') || !this.allowed('correctHistory')) this.activeForm = '';
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

  override hasPendingDraft(): boolean {
    const editChanged = !!this.accountEditOriginal && (this.accountEdit.name !== this.accountEditOriginal.name || this.accountEdit.bankName !== this.accountEditOriginal.bankName || this.accountEdit.isArchived !== this.accountEditOriginal.isArchived);
    return this.accountDraft.name.trim() !== '' || this.accountDraft.bankName.trim() !== '' || this.accountDraft.openingBalance !== '0' || this.accountAdjustment.observedBalance.trim() !== '' || this.accountAdjustment.reason.trim() !== '' || editChanged;
  }

  openForm(form: 'create' | 'adjust' | 'edit'): void {
    if (form === 'adjust' ? !this.allowed('correctHistory') : !this.allowed('manageResources')) return;
    this.activeForm = form;
  }
  closeForm(): void {
    if (!this.saving) this.activeForm = '';
  }

  async saveAccount(): Promise<void> {
    if (!this.allowed('manageResources')) return;
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
    if (!this.allowed('manageResources')) return;
    this.accountEdit = {
      id: account.id,
      name: account.name,
      bankName: account.bankName,
      isArchived: account.isArchived,
      version: account.version,
    };
    this.accountEditOriginal = { ...this.accountEdit };
  }

  async saveAccountEdit(): Promise<void> {
    if (!this.allowed('manageResources')) return;
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
    if (!this.allowed('correctHistory')) return;
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
