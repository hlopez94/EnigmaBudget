import { ChangeDetectorRef, Component, Input, OnChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FinanceOverview, Group, Receivable } from '../core/api.models';
import { ApiService } from '../core/api.service';
import { FinanceActions, financeToday, RefreshFinanceOverview } from './finance-actions';

@Component({
  selector: 'app-sharing',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './sharing.html',
  styleUrl: './finance-shell.css',
})
export class FinanceSharing extends FinanceActions implements OnChanges {
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

  ngOnChanges(): void {
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
    try {
      this.groupReceivables = await this.api.receivables(this.selectedGroupId, this.period);
    } catch (error) {
      this.showLoadError(error);
    }
    this.changeDetector.markForCheck();
  }

  async createGroup(): Promise<void> {
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
    await this.refreshOverview();
  }

  async saveRule(): Promise<void> {
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
      ))
    )
      return;
    await this.refreshOverview();
    await this.loadReceivables();
  }

  async confirmRule(): Promise<void> {
    const group = this.selectedGroup;
    if (!group?.rule) return;
    if (
      !(await this.runMutation(
        () => this.api.confirmRule(group.id, this.period, { version: group.rule!.version }),
        'Reparto del mes confirmado.',
      ))
    )
      return;
    await this.refreshOverview();
    await this.loadReceivables();
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
    const group = this.selectedGroup;
    if (!group) return;
    const draft = { ...this.collection };
    const payload = {
      installmentId: row.sourceId,
      participantId: row.participantId,
      accountId: draft.accountId,
      date: draft.date,
      amount: this.decimal(draft.amount),
      appliedAmount: this.decimal(draft.appliedAmount),
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
