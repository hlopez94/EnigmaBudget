export type Currency = 'ARS' | 'USD';
export interface SessionInfo {
  authenticated: boolean;
  displayName: string | null;
  googleConfigured: boolean;
  developmentLoginEnabled: boolean;
  csrfToken: string;
}
export interface AccountInput {
  id: string;
  name: string;
  bankName: string;
  currency: Currency;
  openingBalance: string;
  openingDate: string;
}
export interface CardInput {
  id: string;
  name: string;
  bankName: string;
  closingDay: number | null;
  dueDay: number | null;
  statementDebtArs: string;
  statementDebtUsd: string;
  asOfDate: string;
}
export interface SaveDraftRequest {
  version: number;
  accounts: AccountInput[];
  cards: CardInput[];
}
export interface CompleteDraftRequest {
  version: number;
}
export interface OnboardingState extends SaveDraftRequest {
  completed: boolean;
}
export interface AccountSummary extends AccountInput {}
export interface CardSummary extends CardInput {}

export type OperationKind = 'Income' | 'Expense';
export interface Account extends AccountInput {
  currentBalance: string;
  isArchived: boolean;
  version: number;
}
export interface Card extends CardInput {
  debtArs: string;
  debtUsd: string;
  creditArs: string;
  creditUsd: string;
  isArchived: boolean;
  version: number;
}
export interface Payment {
  id: string;
  accountId: string;
  date: string;
  amount: string;
}
export interface Operation {
  id: string;
  kind: OperationKind;
  accountId: string;
  date: string;
  description: string;
  category: string;
  amount: string;
  currency: Currency;
  paidAmount: string;
  pendingAmount: string;
  status: 'Pending' | 'Partial' | 'Settled' | 'Voided';
  version: number;
  payments: Payment[];
}
export interface Participant {
  id: string;
  name: string;
  isSelf: boolean;
}
export interface Weight {
  id: string;
  value: string;
}
export interface MonthlyRule {
  period: string;
  version: number;
  mode: 'Equal' | 'Percentage' | 'Income';
  basisCurrency: Currency;
  weights: Weight[];
  confirmed: boolean;
}
export interface Group {
  id: string;
  name: string;
  version: number;
  participants: Participant[];
  rule: MonthlyRule | null;
}
export interface Installment {
  id: string;
  purchaseId: string;
  cardId: string;
  description: string;
  currency: Currency;
  number: number;
  count: number;
  period: string;
  amount: string;
  groupId: string | null;
}
export interface Receivable {
  groupId: string;
  sourceId: string;
  purchaseId: string;
  period: string;
  description: string;
  currency: Currency;
  participantId: string;
  participantName: string;
  isSelf: boolean;
  amount: string | null;
  collectedAmount: string;
  pendingAmount: string | null;
  isEstimated: boolean;
  ruleVersion: number;
  monthConfirmed: boolean;
  status: 'MissingRule' | 'Estimated' | 'Pending' | 'Partial' | 'Collected' | 'OwnShare';
}
export interface CardAdjustment {
  id: string;
  description: string;
  category: string | null;
  amount: string;
}
export interface CardPayment {
  id: string;
  cardId: string;
  accountId: string;
  date: string;
  amount: string;
  accountCurrency: Currency;
  currency: Currency;
  appliedAmount: string;
  statementId: string | null;
}
export interface Statement {
  id: string | null;
  cardId: string;
  period: string;
  currency: Currency;
  version: number;
  state: 'Estimated' | 'Draft' | 'Confirmed';
  dueDate: string | null;
  actualTotal: string | null;
  expectedAmount: string;
  carryAmount: string;
  pendingAmount: string;
  creditBalance: string;
  installments: Installment[];
  adjustments: CardAdjustment[];
  payments: CardPayment[];
}
export interface Purchase {
  id: string;
  cardId: string;
  date: string;
  description: string;
  category: string;
  currency: Currency;
  totalAmount: string;
  installmentCount: number;
  firstPeriod: string;
  groupId: string | null;
  installments: Installment[];
}
export interface FinanceOverview {
  period: string;
  accounts: Account[];
  cards: Card[];
  operations: Operation[];
  groups: Group[];
  installments: Installment[];
  receivables: Receivable[];
  budget: {
    cashExpensesArs: string;
    cashExpensesUsd: string;
    cardInstallmentsArs: string;
    cardInstallmentsUsd: string;
    cardChargesArs: string;
    cardChargesUsd: string;
  };
  totals: {
    balanceArs: string;
    balanceUsd: string;
    cardDebtArs: string;
    cardDebtUsd: string;
    cardCreditArs: string;
    cardCreditUsd: string;
  };
}
