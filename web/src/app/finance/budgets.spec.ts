import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { ApiProblem, ApiService } from '../core/api.service';
import { FinanceBudgets } from './budgets';

const response = [{
  categoryId: 'cat-1', categoryName: 'Alimentos', currency: 'ARS' as const,
  budgetId: 'budget-1', version: 4, isConfigured: true, limitAmount: '0.00',
  spentAmount: '120.50', committedCardAmount: '30.00', remainingAmount: '-150.50',
  sources: [{ sourceKind: 'Installment' as const, sourceId: 'purchase-1', componentId: 'installment-3', date: '2026-10-15', description: 'Compra, cuota 3/6', categorySnapshot: 'Alimentos', amount: '30.00' }],
}];
const permissions = { read: true, readGroups: false, recordFinance: false, manageResources: false, correctHistory: false, manageMembers: false, managePrivacy: false };

describe('monthly budget screen', () => {
  it('shows operation and card consumption as an exact decimal total', () => {
    const screen = new FinanceBudgets({} as ApiService, { markForCheck: vi.fn() } as never);

    expect(screen.consumptionAmount({ ...response[0], spentAmount: '0.10', committedCardAmount: '0.20' } as never)).toBe('0.30');
    expect(screen.consumptionAmount(response[0] as never)).toBe('150.50');
  });

  it('does not show a remaining cap for an unconfigured category and names summary adjustments separately', () => {
    const screen = new FinanceBudgets({} as ApiService, { markForCheck: vi.fn() } as never);
    const unconfigured = { ...response[0], budgetId: null, version: null, isConfigured: false, limitAmount: null };

    expect(screen.showsRemainingCap(unconfigured as never)).toBe(false);
    expect(screen.showsRemainingCap({ ...response[0], remainingAmount: '0.00' } as never)).toBe(true);
    expect(screen.isOverBudget({ ...response[0], remainingAmount: '0.00' } as never)).toBe(false);
    expect(screen.isOverBudget({ ...response[0], remainingAmount: '-0.01' } as never)).toBe(true);
    expect(screen.sourceKindLabel('StatementAdjustment')).toBe('Ajuste de resumen');
  });

  it('sends budget reads and writes to the selected workspace with decimal string values', async () => {
    const fetcher = vi.fn(async () => new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const api = new ApiService(fetcher as typeof fetch);
    api.selectWorkspace('space-9');
    await api.budgets('2026-10', 'USD');
    await api.createBudget({ id: 'budget-new', period: '2026-10', categoryId: 'cat-9', currency: 'USD', limitAmount: '12.30' });
    await api.updateBudget('budget-new', { requestId: 'request-1', version: 3, limitAmount: '0.00' });

    expect(fetcher.mock.calls.map(([url]) => url)).toEqual([
      '/api/budgets?period=2026-10&currency=USD', '/api/budgets', '/api/budgets/budget-new',
    ]);
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-Workspace-Id')).toBe('space-9');
    expect(JSON.parse(String(fetcher.mock.calls[1][1]?.body))).toEqual({ id: 'budget-new', period: '2026-10', categoryId: 'cat-9', currency: 'USD', limitAmount: '12.30' });
    expect(JSON.parse(String(fetcher.mock.calls[2][1]?.body))).toEqual({ requestId: 'request-1', version: 3, limitAmount: '0.00' });
    expect(new Headers(fetcher.mock.calls[2][1]?.headers).get('X-Workspace-Id')).toBe('space-9');
  });

  it('clears old currency rows while a new query is pending and ignores stale responses', async () => {
    let resolveUsd!: (value: typeof response) => void;
    const api = {
      budgets: vi.fn((_period: string, currency: string) => currency === 'USD'
        ? new Promise<typeof response>(resolve => { resolveUsd = resolve; })
        : Promise.resolve(response)),
      financeCatalog: vi.fn().mockResolvedValue({ categories: [], merchants: [], incomeSources: [] }),
    };
    const screen = new FinanceBudgets(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'space-1';
    screen.period = '2026-10';
    screen.permissions = permissions;
    screen.currency = 'ARS';
    await screen.load();
    screen.selectedBudget = response[0] as never;
    const oldQuery = screen.setCurrency('USD');
    expect(screen.rows).toEqual([]);
    expect(screen.selectedBudget).toBeNull();
    await screen.setCurrency('ARS');
    resolveUsd([{ ...response[0], categoryName: 'USD row', currency: 'USD' }]);
    await oldQuery;

    expect(api.budgets.mock.calls.map(call => call[1])).toEqual(['ARS', 'USD', 'ARS']);
    expect(screen.currency).toBe('ARS');
    expect(screen.rows[0].categoryName).toBe('Alimentos');
  });

  it('distinguishes a real zero cap from an unbudgeted category', async () => {
    const api = { budgets: vi.fn().mockResolvedValue(response), financeCatalog: vi.fn().mockResolvedValue({ categories: [{ itemId: 'cat-1', name: 'Alimentos', kind: 'Expense', isArchived: false, isDefault: false, version: 1 }, { itemId: 'cat-old', name: 'Antigua', kind: 'Expense', isArchived: true, isDefault: false, version: 1 }], merchants: [], incomeSources: [] }) };
    const screen = new FinanceBudgets(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'space-1';
    screen.period = '2026-10';
    screen.permissions = permissions;
    await screen.load();

    expect(screen.limitLabel(response[0] as never)).toBe('$ 0,00');
    expect(screen.limitLabel({ ...response[0], budgetId: null, version: null, isConfigured: false, limitAmount: null } as never)).toBe('Sin presupuestar');
    expect(screen.activeExpenseCategories.map(category => category.itemId)).toEqual(['cat-1']);
  });

  it('retains the budget version and original request ID after stale-version failure', async () => {
    const updateBudget = vi.fn().mockRejectedValue(new ApiProblem(409, 'Conflicto'));
    const api = { budgets: vi.fn().mockResolvedValue(response), financeCatalog: vi.fn().mockResolvedValue({ categories: [], merchants: [], incomeSources: [] }), updateBudget };
    const screen = new FinanceBudgets(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'space-1';
    screen.permissions = { ...permissions, manageResources: true };
    await screen.load();
    screen.openLimit(response[0] as never);
    screen.limitDraft = '250.00';

    await screen.saveLimit();
    const firstRequestId = updateBudget.mock.calls[0][1].requestId;
    await screen.saveLimit();

    expect(updateBudget.mock.calls[0][1]).toMatchObject({ requestId: firstRequestId, version: 4, limitAmount: '250.00' });
    expect(updateBudget.mock.calls[1][1].requestId).toBe(firstRequestId);
    expect(screen.limitDraft).toBe('250.00');
    expect(screen.selectedBudget?.version).toBe(4);
    expect(screen.hasStaleError()).toBe(true);
    expect(screen.canManage).toBe(true);
  });

  it('opens factual installment source details using the backend source identity', async () => {
    const api = { budgets: vi.fn().mockResolvedValue(response), financeCatalog: vi.fn().mockResolvedValue({ categories: [], merchants: [], incomeSources: [] }) };
    const screen = new FinanceBudgets(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'space-1';
    screen.period = '2026-10';
    screen.permissions = permissions;
    await screen.load();
    const source = response[0].sources[0];
    screen.openSource(response[0] as never, source as never);

    expect(screen.selectedSource).toMatchObject({ identity: 'Installment:purchase-1:installment-3', label: 'Compra, cuota 3/6', category: 'Alimentos', dateLabel: 'Fecha de compra' });
  });
});
