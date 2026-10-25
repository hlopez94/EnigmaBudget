import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../core/api.service';
import { FinanceSavings } from './savings';

const detector = { markForCheck: vi.fn() } as never;
const permissions = { read: true, readGroups: false, recordFinance: false, manageResources: false, correctHistory: false, manageMembers: false, managePrivacy: false };

describe('savings screens', () => {
  it('retains reader access to goals and balances while removing all mutation actions', async () => {
    const api = { savingsGoals: vi.fn().mockResolvedValue([{ id: 'goal', name: 'Reserva', targetCurrency: 'ARS', targetAmount: '35000.50', state: 'Active', backingAmounts: [], estimateStatus: 'Unavailable', estimateExplanation: 'Falta una tasa.', version: 1 }]), savingsBalances: vi.fn().mockResolvedValue([]), exchangeRates: vi.fn().mockRejectedValue(new Error('rates unavailable')) };
    const screen = new FinanceSavings(api as unknown as ApiService, detector);
    screen.workspaceId = 'space-a'; screen.permissions = permissions;
    await screen.load();
    screen.openCreate(); screen.openAllocate('goal');
    expect(screen.goals[0].targetAmount).toBe('35000.50');
    expect(screen.dialog).toBe('');
    expect(screen.ratesStatus).toContain('No se pudieron consultar');
  });

  it('clears old goal details and drafts when the workspace context changes', async () => {
    const api = { savingsGoals: vi.fn().mockResolvedValue([]), savingsBalances: vi.fn().mockResolvedValue([]), exchangeRates: vi.fn().mockResolvedValue({ snapshots: [], stale: false }) };
    const screen = new FinanceSavings(api as unknown as ApiService, detector);
    screen.workspaceId = 'space-a'; screen.goals = [{ id: 'a', name: 'A', targetCurrency: 'USD', targetAmount: '2', backingAmounts: [], version: 1 } as never];
    screen.goalDraft.name = 'Borrador anterior'; screen.selectedGoalId = 'a';
    (screen as unknown as { invalidateContext: () => void }).invalidateContext.call(screen);
    expect(screen.goals).toEqual([]); expect(screen.selectedGoalId).toBe(''); expect(screen.goalDraft.name).toBe('');
  });

  it('keeps release and application drafts and their idempotency keys after server errors', async () => {
    const allocation = { id: 'allocation-1', goalId: 'goal-1', accountId: 'account-1', currency: 'ARS', reservedAmount: '40.00', version: 1 };
    const api = { releaseSavings: vi.fn().mockRejectedValue(new Error('conflicto de versión')), applySavings: vi.fn().mockRejectedValue(new Error('pago ya aplicado')), savingsGoals: vi.fn().mockResolvedValue([]), savingsBalances: vi.fn().mockResolvedValue([]), exchangeRates: vi.fn().mockResolvedValue({ snapshots: [], stale: false }), savingsGoalDetails: vi.fn().mockResolvedValue({ goal: {}, allocations: [allocation], movements: [], historyPage: 1, historyPageSize: 20, totalMovements: 0, hasMore: false }) };
    const screen = new FinanceSavings(api as unknown as ApiService, detector); screen.workspaceId = 'space'; screen.permissions = { ...permissions, recordFinance: true };
    screen.goals = [{ id: 'goal-1', name: 'Fondo', targetCurrency: 'ARS', targetAmount: '100.00', state: 'Active', backingAmounts: [], version: 1 } as never]; screen.selectedGoalId = 'goal-1'; screen.details = { goal: screen.goals[0], allocations: [allocation as never], movements: [], historyPage: 1, historyPageSize: 20, totalMovements: 0, hasMore: false };
    screen.openRelease(allocation as never); screen.releaseDraft.amount = '12.35'; await screen.saveRelease(); const releaseId = screen.lastRequestId;
    expect(screen.dialog).toBe('release'); expect(screen.releaseDraft.amount).toBe('12.35');
    screen.closeDialog(); screen.openApply(allocation as never); screen.applicationDraft.paymentId = 'real-payment'; screen.applicationDraft.amount = '8.20'; await screen.saveApplication();
    expect(screen.dialog).toBe('apply'); expect(screen.applicationDraft.paymentId).toBe('real-payment'); expect(screen.applicationDraft.amount).toBe('8.20');
    expect(api.releaseSavings.mock.calls[0][1].id).toBe(releaseId);
    expect(api.applySavings.mock.calls[0][0].financePaymentId).toBe('real-payment');
  });

  it('saves the account availability date using the latest overview version and exact string amounts', async () => {
    const api = { setAccountLiquidity: vi.fn().mockResolvedValue({}), savingsGoals: vi.fn().mockResolvedValue([]), savingsBalances: vi.fn().mockResolvedValue([]), exchangeRates: vi.fn().mockResolvedValue({ snapshots: [], stale: false }) };
    const screen = new FinanceSavings(api as unknown as ApiService, detector); screen.workspaceId = 'space'; screen.permissions = { ...permissions, manageResources: true }; screen.refreshOverview = vi.fn().mockResolvedValue(undefined);
    screen.overview = { accounts: [{ id: 'account-1', name: 'Caja', currency: 'ARS', currentBalance: '30000.10', version: 7, isArchived: false }] } as never;
    screen.liquidityDraft = { accountId: 'account-1', fundsAvailableFrom: '2026-11-05' }; await screen.saveLiquidity();
    expect(api.setAccountLiquidity).toHaveBeenCalledWith('account-1', expect.objectContaining({ version: 7, fundsAvailableFrom: '2026-11-05' }));
  });

  it('keeps a manual exchange rate as an eight-decimal string with its reason', async () => {
    const api = { setSavingsGoalRate: vi.fn().mockResolvedValue({}), savingsGoals: vi.fn().mockResolvedValue([]), savingsBalances: vi.fn().mockResolvedValue([]), exchangeRates: vi.fn().mockResolvedValue({ snapshots: [], stale: false }) };
    const screen = new FinanceSavings(api as unknown as ApiService, detector); screen.workspaceId = 'space'; screen.permissions = { ...permissions, manageResources: true }; screen.refreshOverview = vi.fn().mockResolvedValue(undefined);
    screen.goals = [{ id: 'goal-1', name: 'Viaje', targetCurrency: 'USD', targetAmount: '100.00', state: 'Active', backingAmounts: [], version: 3 } as never]; screen.selectedGoalId = 'goal-1';
    screen.rateDraft = { mode: 'Manual', snapshotId: '', house: '', side: '', manualRate: '1234.12345678', manualReason: 'Acuerdo del día' }; await screen.saveRate();
    expect(api.setSavingsGoalRate).toHaveBeenCalledWith('goal-1', expect.objectContaining({ manualArsPerUsd: '1234.12345678', manualReason: 'Acuerdo del día', snapshotId: null, side: null }));
  });
});
