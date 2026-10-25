import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../app';
import { ApiService } from '../core/api.service';
import { normalizeDecimal } from '../onboarding/draft';
import { FinanceAccounts } from './accounts';
import { FinanceOperations } from './operations';
import { FinanceSharing } from './sharing';

describe('finance workspace regressions', () => {
  it('opens usable finance actions for an authenticated user before onboarding is complete', async () => {
    const api = {
      session: vi.fn().mockResolvedValue({
        authenticated: true,
        displayName: 'Ana',
        googleConfigured: false,
        developmentLoginEnabled: false,
        csrfToken: 'csrf',
      }),
      loadOnboarding: vi
        .fn()
        .mockResolvedValue({ version: 0, completed: false, accounts: [], cards: [] }),
      financeOverview: vi.fn().mockResolvedValue({
        period: '2026-10',
        accounts: [],
        cards: [],
        operations: [],
        groups: [],
        installments: [],
        receivables: [],
        budget: {},
        totals: {},
      }),
    };
    const app = new App(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );

    await app.load();

    expect(app.financeAvailable).toBe(true);
  });

  it('normalizes comma decimals to invariant API strings without using floating point', () => {
    expect(normalizeDecimal(' 1250,30 ')).toBe('1250.30');
    expect(normalizeDecimal('900,5')).toBe('900.5');
    expect(normalizeDecimal('900.50')).toBe('900.50');
  });

  it('keeps a pending form available when a save fails and refreshes only after success', async () => {
    let rejectSave = true;
    const api = {
      financeOverview: vi.fn().mockResolvedValue({
        period: '2026-10',
        accounts: [],
        cards: [],
        operations: [],
        groups: [],
        installments: [],
        receivables: [],
        budget: {},
        totals: {},
      }),
      createOperation: vi
        .fn()
        .mockImplementation(() =>
          rejectSave ? Promise.reject(new Error('Conflicto')) : Promise.resolve({ id: 'op' }),
        ),
    };
    const operations = new FinanceOperations(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    operations.overview = { accounts: [{ id: 'account', isArchived: false }] } as never;
    const refresh = vi.fn().mockResolvedValue(undefined);
    operations.refreshOverview = refresh;
    operations.operationDraft = {
      kind: 'Expense',
      accountId: 'account',
      date: '2026-10-24',
      description: 'Supermercado',
      category: 'Comida',
      amount: '1200.50',
      paid: false,
    };

    await operations.saveOperation();
    expect(operations.operationDraft.description).toBe('Supermercado');
    expect(operations.error).toBe('Conflicto');
    expect(api.financeOverview).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();

    const attemptedId = api.createOperation.mock.calls[0][0].id;
    rejectSave = false;
    await operations.saveOperation();
    expect(api.createOperation).toHaveBeenLastCalledWith(
      expect.objectContaining({ amount: '1200.50', paid: false }),
    );
    expect(api.createOperation.mock.calls[1][0].id).toBe(attemptedId);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(operations.operationDraft.description).toBe('');

    operations.operationDraft = {
      kind: 'Expense',
      accountId: 'account',
      date: '2026-10-24',
      description: 'Supermercado',
      category: 'Comida',
      amount: '1200.50',
      paid: false,
    };
    await operations.saveOperation();
    expect(api.createOperation.mock.calls[2][0].id).not.toBe(attemptedId);
  });

  it('refreshes the selected period after changing it without losing the selection', async () => {
    const api = {
      financeOverview: vi.fn().mockImplementation((period: string) =>
        Promise.resolve({
          period,
          accounts: [],
          cards: [],
          operations: [],
          groups: [],
          installments: [],
          receivables: [],
          budget: {},
          totals: {},
        }),
      ),
    };
    const shell = new (await import('./finance-shell')).FinanceShell(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    shell.period = '2026-11';

    await shell.refresh();

    expect(api.financeOverview).toHaveBeenCalledWith('2026-11');
    expect(shell.overview?.period).toBe('2026-11');
  });

  it('keeps the real account form unchanged after a server conflict', async () => {
    const api = { createAccount: vi.fn().mockRejectedValue(new Error('Versión desactualizada')) };
    const refresh = vi.fn().mockResolvedValue(undefined);
    const accounts = new FinanceAccounts(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    accounts.overview = { accounts: [] } as never;
    accounts.refreshOverview = refresh;
    accounts.accountDraft = {
      id: 'new-account',
      name: 'Caja ahorro',
      bankName: 'Banco',
      currency: 'ARS',
      openingBalance: '15200,40',
      openingDate: '2026-10-24',
    };

    await accounts.saveAccount();

    expect(accounts.accountDraft.name).toBe('Caja ahorro');
    expect(accounts.error).toBe('Versión desactualizada');
    expect(refresh).not.toHaveBeenCalled();
    expect(api.createAccount).toHaveBeenCalledWith(
      expect.objectContaining({ openingBalance: '15200.40' }),
    );
  });

  it('blocks duplicate submissions from the actual movement section while an API save is pending', async () => {
    let finish!: () => void;
    const pending = new Promise<void>((resolve) => {
      finish = resolve;
    });
    const api = { createOperation: vi.fn().mockReturnValue(pending) };
    const operations = new FinanceOperations(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    operations.overview = { accounts: [{ id: 'a', isArchived: false }] } as never;
    operations.refreshOverview = vi.fn().mockResolvedValue(undefined);
    operations.operationDraft = {
      kind: 'Expense',
      accountId: 'a',
      date: '2026-10-24',
      description: 'Luz',
      category: '',
      amount: '900,25',
      paid: false,
    };

    const save = operations.saveOperation();
    await operations.saveOperation();
    expect(api.createOperation).toHaveBeenCalledTimes(1);
    expect(operations.operationDraft.description).toBe('Luz');
    finish();
    await save;
    expect(api.createOperation).toHaveBeenCalledWith(expect.objectContaining({ amount: '900.25' }));
  });

  it('sends monthly group weights using participantId and preserves the selected group', async () => {
    const api = {
      saveRule: vi.fn().mockResolvedValue(undefined),
      receivables: vi.fn().mockResolvedValue([]),
    };
    const sharing = new FinanceSharing(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    sharing.period = '2026-11';
    sharing.overview = {
      groups: [
        {
          id: 'group-1',
          name: 'Viaje',
          version: 1,
          participants: [
            { id: 'person-a', name: 'Ana', isSelf: true },
            { id: 'person-b', name: 'Beto', isSelf: false },
          ],
          rule: {
            period: '2026-11',
            version: 3,
            mode: 'Percentage',
            basisCurrency: 'ARS',
            weights: [],
            confirmed: false,
          },
        },
      ],
    } as never;
    sharing.selectedGroupId = 'group-1';
    sharing.ruleMode = 'Percentage';
    sharing.ruleWeights = { 'person-a': '30,00', 'person-b': '70,00' };
    sharing.refreshOverview = vi.fn().mockResolvedValue(undefined);

    await sharing.saveRule();

    expect(sharing.selectedGroupId).toBe('group-1');
    expect(api.saveRule).toHaveBeenCalledWith('group-1', '2026-11', {
      version: 3,
      mode: 'Percentage',
      basisCurrency: 'ARS',
      weights: [
        { participantId: 'person-a', value: '30.00' },
        { participantId: 'person-b', value: '70.00' },
      ],
    });
  });
});
