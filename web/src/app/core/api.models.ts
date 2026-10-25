export type Currency = 'ARS' | 'USD';

export interface SavingsGoalCreate { id: string; name: string; targetCurrency: Currency; targetAmount: string; desiredDate: string | null; monthlyContribution: string | null; nextContributionDate: string | null; }
export interface SavingsGoalUpdate { requestId: string; version: number; name: string; targetCurrency: Currency; targetAmount: string; desiredDate: string | null; state: string; monthlyContribution: string | null; nextContributionDate: string | null; }
export interface BackingAmount { currency: Currency; amount: string; }
export type SavingsRateSide = 'Compra' | 'Venta';
export interface GoalRateChoice { mode: string; snapshotId: string | null; house: string | null; side: SavingsRateSide | null; rate: string; providerUpdatedAt: string | null; fetchedAt: string | null; manualReason: string | null; stale: boolean; }
export interface SavingsGoal { id: string; name: string; targetCurrency: Currency; targetAmount: string; desiredDate: string | null; state: string; monthlyContribution: string | null; nextContributionDate: string | null; reached: boolean; backingAmounts: BackingAmount[]; rateChoice: GoalRateChoice | null; savedAmount: string | null; remainingAmount: string | null; requiredMonthlyContribution: string | null; requiredContributionCount: number | null; estimatedCompletionDate: string | null; estimateStatus: string; estimateExplanation: string; version: number; }
export interface SavingsAllocation { id: string; goalId: string; accountId: string; currency: Currency; reservedAmount: string; version: number; }
export interface SavingsReleaseRequest { id: string; amount: string; }
export interface SavingsAccountBalance { accountId: string; currency: Currency; cashBalance: string; availableCash: string; restrictedCash: string; totalReserved: string; backingDeficit: string; freeBalance: string; isArchived: boolean; fundsAvailableFrom: string | null; }
export interface SavingsMovement { id: string; allocationId: string; kind: string; currency: Currency; amount: string; financePaymentId: string | null; reversesMovementId: string | null; reason: string | null; actorUserId: string; actorDisplayName: string; occurredAt: string; isReversed: boolean; }
export interface SavingsGoalDetails { goal: SavingsGoal; allocations: SavingsAllocation[]; movements: SavingsMovement[]; historyPage: number; historyPageSize: number; totalMovements: number; hasMore: boolean; }
export interface SavingsRateChoiceInput { requestId: string; goalVersion: number; mode: string; snapshotId: string | null; house: string | null; side: SavingsRateSide | null; manualArsPerUsd: string | null; manualReason: string | null; }
export interface ExchangeRateSnapshot { id: string; currency: string; house: string; name: string; buy: string | null; sell: string | null; providerUpdatedAt: string; fetchedAt: string; }
export interface ExchangeRateList { status: string; stale: boolean; error: string | null; fetchedAt: string | null; snapshots: ExchangeRateSnapshot[]; }
export interface ProjectionDailyBalance { date: string; cashBalance: string; availableCash: string; restrictedCash: string; reservedNominal: string; freeBalance: string; }
export interface CurrencyProjection { currency: Currency; cashBalance: string; availableCash: string; restrictedCash: string; reservedNominal: string; backingDeficit: string; freeBalance: string; minimumFreeBalance: string; firstShortfallDate: string | null; dailyBalances: ProjectionDailyBalance[]; }
export interface ProjectionRow { sourceKind: string; sourceId: string; componentId: string | null; date: string | null; dateCertainty: string; currency: Currency; amount: string; direction: string; certainty: string; accountId: string | null; description: string; summaryId: string | null; warnings: string[]; }
export interface ProjectionAlert { code: string; severity: string; currency: Currency; message: string; date: string | null; sourceId: string | null; }
export interface Projection { workspaceId: string; workspaceName: string; asOf: string; currencies: CurrencyProjection[]; rows: ProjectionRow[]; alerts: ProjectionAlert[]; }
export interface ProjectionAssumption { id: string; kind: 'Income' | 'Expense' | 'AmountChange' | 'DateChange'; date: string | null; currency: Currency | null; amount: string | null; description: string; sourceId: string | null; sourceKind?: string | null; componentId?: string | null; }
export interface ProjectionCalculateRequest { through: string; scenario: string; savedScenarioId: string | null; assumptions: ProjectionAssumption[]; }
export interface ProjectionScenario { id: string; name: string; version: number; assumptions: ProjectionAssumption[]; }
export interface ProjectionScenarioCreate { id: string; name: string; assumptions: ProjectionAssumption[]; }
export interface ProjectionScenarioSave { id: string; requestId: string; version: number; name: string; assumptions: ProjectionAssumption[]; }
export interface ProjectionScenarioDelete { requestId: string; version: number; }
export interface SessionInfo {
  authenticated: boolean;
  displayName: string | null;
  googleConfigured: boolean;
  developmentLoginEnabled: boolean;
  developmentUsers: { key: string; displayName: string }[];
  csrfToken: string;
}
export type WorkspaceRole = 'Owner' | 'Admin' | 'Member' | 'Reader';
export interface WorkspacePermissions {
  read: boolean;
  readGroups: boolean;
  recordFinance: boolean;
  manageResources: boolean;
  correctHistory: boolean;
  manageMembers: boolean;
  managePrivacy: boolean;
}
export interface Workspace {
  id: string;
  name: string;
  ownerDisplayName: string | null;
  membershipVersion: number | null;
  kind: 'Personal' | 'Household';
  role: WorkspaceRole;
  isSharedPersonalRead: boolean;
  isArchived: boolean;
  version: number;
  permissions: WorkspacePermissions;
}
export interface WorkspaceMember {
  userId: string;
  displayName: string;
  role: WorkspaceRole;
  active: boolean;
  version: number;
}
export interface WorkspaceInvitation {
  id: string;
  email: string;
  role: Exclude<WorkspaceRole, 'Owner'>;
  status: 'Pending' | 'Expired' | 'Revoked' | 'Accepted';
  expiresAt: string;
  version: number;
  shareUrl: string | null;
}
export interface PersonalSharing {
  householdId: string;
  householdName: string;
  enabled: boolean;
  version: number;
  sharedWithUserIds: string[];
  needsAudienceRenewal: boolean;
}
export interface InvitationPreview {
  workspaceId: string;
  workspaceName: string;
  role: Exclude<WorkspaceRole, 'Owner'>;
  expiresAt: string;
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
