import { ChangeDetectorRef, Component, Input, OnChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CardAdjustment, Currency, FinanceOverview, Purchase, Statement } from '../core/api.models';
import { ApiService } from '../core/api.service';
import {
  FinanceActions,
  financeId,
  financePeriod,
  financeToday,
  RefreshFinanceOverview,
} from './finance-actions';

@Component({
  selector: 'app-cards',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './cards.html',
  styleUrl: './finance-shell.css',
})
export class FinanceCards extends FinanceActions implements OnChanges {
  @Input({ required: true }) overview!: FinanceOverview;
  @Input() period = financePeriod();
  @Input({ required: true }) refreshOverview!: RefreshFinanceOverview;
  selectedCardId = '';
  statement: Statement | null = null;
  purchases: Purchase[] = [];
  cardDraft = {
    id: financeId(),
    name: '',
    bankName: '',
    closingDay: '',
    dueDay: '',
    statementDebtArs: '0',
    statementDebtUsd: '0',
    asOfDate: financeToday(),
  };
  cardEdit = {
    id: '',
    name: '',
    bankName: '',
    closingDay: '',
    dueDay: '',
    isArchived: false,
    version: 0,
  };
  purchaseDraft = {
    date: financeToday(),
    description: '',
    category: '',
    currency: 'ARS' as Currency,
    totalAmount: '',
    installmentCount: 1,
    firstPeriod: financePeriod(),
    groupId: '',
  };
  statementDraft = {
    currency: 'ARS' as Currency,
    dueDate: '',
    actualTotal: '',
    adjustments: [] as CardAdjustment[],
  };
  cardPayment = {
    accountId: '',
    date: financeToday(),
    amount: '',
    appliedAmount: '',
    currency: 'ARS' as Currency,
  };

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) {
    super(api, changeDetector);
  }
  get cards() {
    return this.overview?.cards ?? [];
  }
  get accounts() {
    return this.overview?.accounts ?? [];
  }
  get activeAccounts() {
    return this.accounts.filter((account) => !account.isArchived);
  }
  get groups() {
    return this.overview?.groups ?? [];
  }
  get selectedCard() {
    return this.cards.find((card) => card.id === this.selectedCardId);
  }

  ngOnChanges(): void {
    if (!this.selectedCardId || !this.cards.some((card) => card.id === this.selectedCardId)) {
      this.selectedCardId = this.cards[0]?.id ?? '';
    }
    if (this.selectedCardId) {
      void this.loadStatement();
      void this.loadPurchases();
    } else {
      this.statement = null;
      this.purchases = [];
    }
  }

  selectCard(cardId: string): void {
    this.selectedCardId = cardId;
    this.cardEdit.id = '';
    void this.loadStatement();
    void this.loadPurchases();
  }

  editCard(card: NonNullable<FinanceCards['selectedCard']>): void {
    this.selectedCardId = card.id;
    this.cardEdit = {
      id: card.id,
      name: card.name,
      bankName: card.bankName,
      closingDay: card.closingDay?.toString() ?? '',
      dueDay: card.dueDay?.toString() ?? '',
      isArchived: card.isArchived,
      version: card.version,
    };
  }

  async loadStatement(): Promise<void> {
    if (!this.selectedCardId) return;
    this.statement = null;
    try {
      this.statement = await this.api.statement(
        this.selectedCardId,
        this.period,
        this.statementDraft.currency,
      );
      this.statementDraft.dueDate = this.statement.dueDate ?? '';
      this.statementDraft.actualTotal = this.statement.actualTotal ?? this.statement.expectedAmount;
      this.statementDraft.adjustments = this.statement.adjustments.map((adjustment) => ({
        ...adjustment,
        category: adjustment.category ?? '',
      }));
      this.cardPayment.currency = this.statement.currency;
    } catch (error) {
      this.error = this.message(error);
    }
    this.changeDetector.markForCheck();
  }

  async loadPurchases(): Promise<void> {
    if (!this.selectedCardId) return;
    this.purchases = [];
    try {
      this.purchases = await this.api.purchases(this.selectedCardId);
    } catch (error) {
      this.error = this.message(error);
    }
    this.changeDetector.markForCheck();
  }

  async saveCard(): Promise<void> {
    const draft = {
      ...this.cardDraft,
      closingDay: this.day(this.cardDraft.closingDay),
      dueDay: this.day(this.cardDraft.dueDay),
      statementDebtArs: this.decimal(this.cardDraft.statementDebtArs),
      statementDebtUsd: this.decimal(this.cardDraft.statementDebtUsd),
    };
    if (!(await this.runMutation(() => this.api.createCard(draft), 'Tarjeta creada.'))) return;
    this.cardDraft = {
      id: financeId(),
      name: '',
      bankName: '',
      closingDay: '',
      dueDay: '',
      statementDebtArs: '0',
      statementDebtUsd: '0',
      asOfDate: financeToday(),
    };
    await this.refreshOverview();
  }

  async saveCardEdit(): Promise<void> {
    const draft = { ...this.cardEdit };
    if (
      !(await this.runMutation(
        () =>
          this.api.updateCard(draft.id, {
            version: draft.version,
            name: draft.name,
            bankName: draft.bankName,
            closingDay: this.day(draft.closingDay),
            dueDay: this.day(draft.dueDay),
            isArchived: draft.isArchived,
          }),
        'Tarjeta actualizada.',
      ))
    )
      return;
    this.cardEdit.id = '';
    await this.refreshOverview();
  }

  async savePurchase(): Promise<void> {
    const draft = { ...this.purchaseDraft };
    const payload = {
      cardId: this.selectedCardId,
      ...draft,
      category: draft.category || null,
      totalAmount: this.decimal(draft.totalAmount),
      groupId: draft.groupId || null,
    };
    if (
      !(await this.runMutation(
        () => this.api.createPurchase({ id: this.requestId('purchase', payload), ...payload }),
        'Compra y calendario de cuotas guardados.',
      ))
    )
      return;
    this.releaseRequestId('purchase', payload);
    this.purchaseDraft = {
      date: financeToday(),
      description: '',
      category: '',
      currency: 'ARS',
      totalAmount: '',
      installmentCount: 1,
      firstPeriod: this.period,
      groupId: '',
    };
    await this.refreshOverview();
  }

  async saveStatement(): Promise<void> {
    if (!this.selectedCardId) return;
    const statement = this.statement;
    const adjustments = this.statementDraft.adjustments.map((adjustment) => ({
      id: adjustment.id || financeId(),
      description: adjustment.description,
      category: adjustment.category || null,
      amount: this.decimal(adjustment.amount),
    }));
    const payload = {
      period: this.period,
      currency: this.statementDraft.currency,
      version: statement?.version ?? 0,
      dueDate: this.statementDraft.dueDate || null,
      actualTotal: this.decimal(this.statementDraft.actualTotal),
      adjustments,
    };
    const request = {
      id: statement?.id ?? this.requestId(`statement:${this.selectedCardId}`, payload),
      ...payload,
    };
    if (
      !(await this.runMutation(
        () => this.api.saveStatement(this.selectedCardId, request),
        'Borrador del resumen guardado.',
      ))
    )
      return;
    if (!statement?.id) this.releaseRequestId(`statement:${this.selectedCardId}`, payload);
    await this.refreshOverview();
    await this.loadStatement();
  }

  addStatementAdjustment(): void {
    this.statementDraft.adjustments = [
      ...this.statementDraft.adjustments,
      { id: financeId(), description: '', category: '', amount: '' },
    ];
  }
  removeStatementAdjustment(index: number): void {
    this.statementDraft.adjustments = this.statementDraft.adjustments.filter(
      (_, current) => current !== index,
    );
  }

  async confirmStatement(): Promise<void> {
    if (!this.selectedCardId || !this.statement?.id) return;
    const statement = this.statement;
    if (
      !(await this.runMutation(
        () =>
          this.api.confirmStatement(this.selectedCardId, {
            id: statement.id,
            version: statement.version,
          }),
        'Resumen confirmado.',
      ))
    )
      return;
    await this.refreshOverview();
    await this.loadStatement();
  }

  async payStatement(): Promise<void> {
    if (!this.selectedCardId || !this.statement) return;
    const statement = this.statement;
    const draft = { ...this.cardPayment };
    const payload = {
      accountId: draft.accountId,
      date: draft.date,
      amount: this.decimal(draft.amount),
      currency: statement.currency,
      appliedAmount: this.decimal(draft.appliedAmount || draft.amount),
      statementId: statement.id,
    };
    if (
      !(await this.runMutation(
        () =>
          this.api.payCard(this.selectedCardId, {
            id: this.requestId(`card-payment:${this.selectedCardId}`, payload),
            ...payload,
          }),
        'Pago de tarjeta registrado.',
      ))
    )
      return;
    this.releaseRequestId(`card-payment:${this.selectedCardId}`, payload);
    this.cardPayment = {
      accountId: '',
      date: financeToday(),
      amount: '',
      appliedAmount: '',
      currency: statement.currency,
    };
    await this.refreshOverview();
    await this.loadStatement();
  }
}
