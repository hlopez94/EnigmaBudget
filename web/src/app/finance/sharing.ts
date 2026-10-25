import { ChangeDetectorRef, Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FinanceOverview, Group, Receivable } from '../core/api.models';
import { ApiService } from '../core/api.service';
import { toMinorUnits } from '../onboarding/draft';
import { FinanceActions, financeToday, RefreshFinanceOverview } from './finance-actions';
import { ActionDialog } from './ui/action-dialog';

@Component({
  selector: 'app-sharing',
  standalone: true,
  imports: [FormsModule, ActionDialog],
  templateUrl: './sharing.html',
  styleUrl: './finance-shell.css',
})
export class FinanceSharing extends FinanceActions implements OnChanges {
  activeForm: '' | 'group' = '';
  @Input({ required: true }) overview!: FinanceOverview;
  @Input() period = '';
  @Input({ required: true }) refreshOverview!: RefreshFinanceOverview;

  selectedGroupId = '';
  groupReceivables: Receivable[] = [];
  groupDraft = { name: '', selfName: 'Yo', others: '' };
  ruleMode: 'Equal' | 'Percentage' | 'Income' = 'Equal';
  ruleCurrency: 'ARS' | 'USD' = 'ARS';
  ruleWeights: Record<string, string> = {};
  collection = {
    participantId: '',
    sourceId: '',
    accountId: '',
    amount: '',
    appliedAmount: '',
    date: financeToday(),
  };

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) {
    super(api, changeDetector);
  }
  protected override permissionsChanged(): void {
    if (this.workspaceArchived || !this.allowed('manageResources')) this.activeForm = '';
  }
  protected override resetForContextChange(): void {
    this.activeForm = '';
    this.selectedGroupId = '';
    this.groupReceivables = [];
    this.groupDraft = { name: '', selfName: 'Yo', others: '' };
    this.ruleMode = 'Equal';
    this.ruleCurrency = 'ARS';
    this.ruleWeights = {};
    this.collection = { participantId: '', sourceId: '', accountId: '', amount: '', appliedAmount: '', date: financeToday() };
  }

  get groups(): Group[] {
    return this.overview?.groups ?? [];
  }
  get accounts() {
    return this.overview?.accounts ?? [];
  }
  get activeAccounts() {
    return this.accounts.filter((account) => !account.isArchived);
  }
  get selectedGroup(): Group | undefined {
    return this.groups.find((group) => group.id === this.selectedGroupId);
  }
  override hasPendingDraft(): boolean {
    const rule = this.selectedGroup?.rule;
    const weightsChanged = rule
      ? this.selectedGroup!.participants.some((participant) => (this.ruleWeights[participant.id] ?? '') !== (rule.weights.find((weight) => weight.id === participant.id)?.value ?? ''))
      : Object.values(this.ruleWeights).some((value) => value.trim() !== '');
    const ruleChanged = weightsChanged || (rule
      ? this.ruleMode !== rule.mode || this.ruleCurrency !== rule.basisCurrency
      : this.ruleMode !== 'Equal' || this.ruleCurrency !== 'ARS');
    return this.groupDraft.name.trim() !== '' || this.groupDraft.selfName.trim() !== 'Yo' || this.groupDraft.others.trim() !== '' || this.collection.amount.trim() !== '' || this.collection.appliedAmount.trim() !== '' || this.collection.participantId !== '' || ruleChanged;
  }
  get collectionCurrenciesDiffer(): boolean {
    const row = this.groupReceivables.find(
      (item) =>
        item.sourceId === this.collection.sourceId &&
        item.participantId === this.collection.participantId,
    );
    const account = this.accounts.find((item) => item.id === this.collection.accountId);
    return !!row && !!account && account.currency !== row.currency;
  }
  openForm(): void {
    if (!this.allowed('manageResources')) return;
    this.activeForm = 'group';
  }
  closeForm(): void {
    if (!this.saving) this.activeForm = '';
  }

  override ngOnChanges(changes: SimpleChanges = {}): void {
    super.ngOnChanges(changes);
    if (!this.groups.some((group) => group.id === this.selectedGroupId)) {
      this.selectedGroupId = this.groups[0]?.id ?? '';
    }
    this.hydrateRule();
    void this.loadReceivables();
  }

  async selectGroup(): Promise<void> {
    this.hydrateRule();
    await this.loadReceivables();
  }

  async loadReceivables(): Promise<void> {
    if (!this.selectedGroupId) {
      this.groupReceivables = [];
      this.changeDetector.markForCheck();
      return;
    }
    const contextEpoch = this.captureContext();
    const groupId = this.selectedGroupId;
    const period = this.period;
    try {
      const receivables = await this.api.receivables(groupId, period);
      if (!this.isCurrentContext(contextEpoch) || this.selectedGroupId !== groupId || this.period !== period) return;
      this.groupReceivables = receivables;
    } catch (error) {
      if (this.isCurrentContext(contextEpoch) && this.selectedGroupId === groupId && this.period === period) this.showLoadError(error);
    }
    if (this.isCurrentContext(contextEpoch) && this.selectedGroupId === groupId && this.period === period) this.changeDetector.markForCheck();
  }

  async createGroup(): Promise<void> {
    if (!this.allowed('manageResources')) return;
    const names = [
      this.groupDraft.selfName,
      ...this.groupDraft.others
        .split('\n')
        .map((name) => name.trim())
        .filter(Boolean),
    ];
    if (names.length < 2) {
      this.error = 'Agregá al menos otra persona para crear el grupo.';
      this.changeDetector.markForCheck();
      return;
    }
    const participantPayloads = names.map((name, index) => ({
      groupName: this.groupDraft.name,
      name,
      index,
    }));
    const participants = names.map((name, index) => ({
      id: this.requestId('participant', participantPayloads[index]),
      name,
      isSelf: index === 0,
    }));
    const payload = { name: this.groupDraft.name, participants };
    const draft = { id: this.requestId('group', payload), ...payload };
    if (!(await this.runMutation(() => this.api.createGroup(draft), 'Grupo creado.'))) return;
    this.releaseRequestId('group', payload);
    participantPayloads.forEach((participant) => this.releaseRequestId('participant', participant));
    this.groupDraft = { name: '', selfName: 'Yo', others: '' };
    this.activeForm = '';
    await this.refreshOverview();
  }

  async saveRule(): Promise<void> {
    if (!this.allowed('manageResources')) return;
    const group = this.selectedGroup;
    if (!group) return;
    const request = {
      version: group.rule?.version ?? 0,
      mode: this.ruleMode,
      basisCurrency: this.ruleCurrency,
      weights: group.participants.map((participant) => ({
        participantId: participant.id,
        value: this.decimal(
          this.ruleWeights[participant.id] ?? (this.ruleMode === 'Equal' ? '1' : '0'),
        ),
      })),
    };
    if (
      !(await this.runMutation(
        () => this.api.saveRule(group.id, this.period, request),
        'Reparto mensual guardado.',
        async () => {
          await this.refreshOverview();
          await this.loadReceivables();
        },
      ))
    )
      return;
  }

  async confirmRule(): Promise<void> {
    if (!this.allowed('manageResources')) return;
    const group = this.selectedGroup;
    if (!group?.rule) return;
    if (
      !(await this.runMutation(
        () => this.api.confirmRule(group.id, this.period, { version: group.rule!.version }),
        'Reparto del mes confirmado.',
        async () => {
          await this.refreshOverview();
          await this.loadReceivables();
        },
      ))
    )
      return;
  }

  chooseReceivable(row: Receivable): void {
    const pending = row.pendingAmount ?? '';
    this.collection = {
      participantId: row.participantId,
      sourceId: row.sourceId,
      accountId: this.activeAccounts.find((account) => account.currency === row.currency)?.id ?? '',
      amount: pending,
      appliedAmount: pending,
      date: financeToday(),
    };
  }

  async collect(row: Receivable): Promise<void> {
    if (!this.allowed('recordFinance')) return;
    const group = this.selectedGroup;
    if (!group) return;
    const draft = { ...this.collection };
    const account = this.activeAccounts.find((item) => item.id === draft.accountId);
    if (!account) {
      this.error = 'Elegí la cuenta donde se recibe el cobro.';
      this.changeDetector.markForCheck();
      return;
    }
    const amount = this.decimal(draft.amount);
    const appliedAmountEntered = draft.appliedAmount.trim() !== '';
    const currenciesDiffer = account.currency !== row.currency;
    if (currenciesDiffer && !appliedAmountEntered) {
      this.error = 'Ingresá el importe real que cancela la deuda del participante.';
      this.changeDetector.markForCheck();
      return;
    }
    const appliedAmount = appliedAmountEntered ? this.decimal(draft.appliedAmount) : amount;
    if (!currenciesDiffer && toMinorUnits(appliedAmount) !== toMinorUnits(amount)) {
      this.error = 'Para la misma moneda, ambos importes del cobro deben coincidir.';
      this.changeDetector.markForCheck();
      return;
    }
    const payload = {
      installmentId: row.sourceId,
      participantId: row.participantId,
      accountId: draft.accountId,
      date: draft.date,
      amount,
      appliedAmount,
      ruleVersion: row.ruleVersion,
    };
    const request = {
      id: this.requestId(`collection:${group.id}`, payload),
      ...payload,
    };
    if (!(await this.runMutation(() => this.api.collect(group.id, request), 'Cobro registrado.')))
      return;
    this.releaseRequestId(`collection:${group.id}`, payload);
    this.collection = {
      participantId: '',
      sourceId: '',
      accountId: '',
      amount: '',
      appliedAmount: '',
      date: financeToday(),
    };
    await this.refreshOverview();
    await this.loadReceivables();
  }

  override money(amount: string, currency: 'ARS' | 'USD'): string {
    return super.money(amount, currency);
  }

  private hydrateRule(): void {
    const rule = this.selectedGroup?.rule;
    if (!rule) {
      this.ruleWeights = {};
      this.ruleMode = 'Equal';
      this.ruleCurrency = 'ARS';
      return;
    }
    this.ruleWeights = Object.fromEntries(rule.weights.map((weight) => [weight.id, weight.value]));
    this.ruleMode = rule.mode;
    this.ruleCurrency = rule.basisCurrency;
  }
}
