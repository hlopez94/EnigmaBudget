export type Currency = 'ARS' | 'USD';
export interface SessionInfo { authenticated: boolean; displayName: string | null; googleConfigured: boolean; developmentLoginEnabled: boolean; csrfToken: string; }
export interface AccountInput { id: string; name: string; bankName: string; currency: Currency; openingBalance: string; openingDate: string; }
export interface CardInput { id: string; name: string; bankName: string; closingDay: number | null; dueDay: number | null; statementDebtArs: string; statementDebtUsd: string; asOfDate: string; }
export interface SaveDraftRequest { version: number; accounts: AccountInput[]; cards: CardInput[]; }
export interface CompleteDraftRequest { version: number; }
export interface OnboardingState extends SaveDraftRequest { completed: boolean; }
export interface AccountSummary extends AccountInput {}
export interface CardSummary extends CardInput {}
