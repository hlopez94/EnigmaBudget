import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../app';
import { ApiService } from '../core/api.service';
import { normalizeDecimal } from '../onboarding/draft';
import { FinanceAccounts } from './accounts';
import { FinanceCards } from './cards';
import { FinanceOperations } from './operations';
import { FinanceSharing } from './sharing';

class TestFinanceOperations extends FinanceOperations {
  idempotencyKey(payload: unknown): string {
    return this.requestId('test-operation', payload);
  }
}

describe('finance workspace regressions', () => {
  it('does not treat the remembered account selection as an unsaved operation draft', () => {
    const operations = new TestFinanceOperations({} as ApiService, { markForCheck: vi.fn() } as never);
    operations.operationDraft.accountId = 'default-account';

    expect(operations.hasPendingDraft()).toBe(false);
  });

  it('keeps a closed finance draft on the old space when switching is cancelled, then discards it only after confirmation', async () => {
    const shell = new (await import('./finance-shell')).FinanceShell(
      { selectWorkspace: vi.fn() } as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    const operations = new TestFinanceOperations({} as ApiService, { markForCheck: vi.fn() } as never);
    operations.operationDraft.description = 'Cena familiar';
    operations.activeForm = '';
    const payload = { description: operations.operationDraft.description };
    const firstKey = operations.idempotencyKey(payload);
    shell.activeWorkspace = { id: 'old-space' } as never;
    shell.operationComponents = { toArray: () => [operations] } as never;
    const selected = vi.fn();
    shell.workspaceSelected.subscribe(selected);

    shell.requestWorkspaceChange('new-space');
    expect(shell.requestedWorkspaceId).toBe('new-space');
    shell.cancelWorkspaceChange();
    expect(operations.operationDraft.description).toBe('Cena familiar');
    expect(operations.idempotencyKey(payload)).toBe(firstKey);
    expect(selected).not.toHaveBeenCalled();

    shell.requestWorkspaceChange('new-space');
    shell.confirmWorkspaceChange();
    expect(selected).toHaveBeenCalledWith('new-space');
    expect(operations.operationDraft.description).toBe('');
    expect(operations.hasPendingDraft()).toBe(false);
    expect(operations.idempotencyKey(payload)).not.toBe(firstKey);
  });

  it('ignores delayed card purchases and errors after its workspace context is invalidated', async () => {
    const oldResponse = deferred<never>();
    const api = { purchases: vi.fn().mockReturnValue(oldResponse.promise) };
    const cards = new FinanceCards(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    cards.selectedCardId = 'old-card';
    const oldLoad = cards.loadPurchases();

    (cards as unknown as { invalidateContext: () => void }).invalidateContext.call(cards);
    cards.selectedCardId = 'new-card';
    api.purchases.mockResolvedValue([{ id: 'new-purchase' }]);
    await cards.loadPurchases();
    oldResponse.reject(new Error('old workspace failed'));
    await oldLoad;

    expect(cards.purchases).toEqual([{ id: 'new-purchase' }]);
    expect(cards.error).toBe('');
  });

  it('ignores delayed receivables errors after its workspace context is invalidated', async () => {
    const oldResponse = deferred<never>();
    const api = { receivables: vi.fn().mockReturnValue(oldResponse.promise) };
    const sharing = new FinanceSharing(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    sharing.selectedGroupId = 'old-group';
    const oldLoad = sharing.loadReceivables();

    (sharing as unknown as { invalidateContext: () => void }).invalidateContext.call(sharing);
    sharing.selectedGroupId = 'new-group';
    api.receivables.mockResolvedValue([{ id: 'new-receivable' }]);
    await sharing.loadReceivables();
    oldResponse.reject(new Error('old workspace failed'));
    await oldLoad;

    expect(sharing.groupReceivables).toEqual([{ id: 'new-receivable' }]);
    expect(sharing.error).toBe('');
  });

  it('keeps card details readable for Readers while denying purchase entry', async () => {
    const api = { purchases: vi.fn().mockResolvedValue([{ id: 'purchase-1' }]) };
    const cards = new FinanceCards(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    cards.permissions = { read: true, readGroups: false, recordFinance: false, manageResources: false, correctHistory: false, manageMembers: false, managePrivacy: false };
    cards.selectedCardId = 'card-1';

    cards.openForm('purchase');
    await cards.loadPurchases();

    expect(cards.activeForm).toBe('');
    expect(api.purchases).toHaveBeenCalledWith('card-1');
    expect(cards.purchases).toEqual([{ id: 'purchase-1' }]);
  });

  it('blocks workspace switching while a finance save is in flight', async () => {
    const shell = new (await import('./finance-shell')).FinanceShell(
      {} as ApiService,
      { markForCheck: vi.fn() } as never,
    );
    const operations = new FinanceOperations({} as ApiService, { markForCheck: vi.fn() } as never);
    operations.saving = true;
    shell.operationComponents = { toArray: () => [operations] } as never;

    shell.requestWorkspaceChange('new-space');

    expect(shell.requestedWorkspaceId).toBe('');
    expect(shell.notice).toMatch(/guardado/i);
  });

  it('hides resource creation from Members while preserving their finance entry actions', async () => {
    const api = { createAccount: vi.fn(), adjustAccount: vi.fn() };
    const accounts = new FinanceAccounts(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    accounts.permissions = { read: true, readGroups: true, recordFinance: true, manageResources: false, correctHistory: false, manageMembers: false, managePrivacy: false };
    accounts.openForm('create');
    await accounts.saveAccount();
    accounts.openForm('adjust');
    await accounts.adjustAccount();

    const operations = new FinanceOperations({} as ApiService, { markForCheck: vi.fn() } as never);
    operations.permissions = accounts.permissions;
    operations.openForm('operation');

    expect(accounts.activeForm).toBe('');
    expect(operations.activeForm).toBe('operation');
    expect(api.createAccount).not.toHaveBeenCalled();
    expect(api.adjustAccount).not.toHaveBeenCalled();
  });

  it('ignores an old overview response after the active workspace changes', async () => {
    let finishOld!: (value: never) => void;
    const oldOverview = new Promise<never>((resolve) => { finishOld = resolve; });
    let selectedId = '';
    const api = {
      selectWorkspace: vi.fn((id: string) => { selectedId = id; }),
      workspace: vi.fn(() => Promise.resolve({ id: selectedId, permissions: { read: true, readGroups: true, recordFinance: true, manageResources: true, correctHistory: true, manageMembers: true, managePrivacy: true }, isArchived: false })),
      financeOverview: vi.fn(() => selectedId === 'old-space' ? oldOverview : Promise.resolve({ period: '2026-11', marker: 'new-space' })),
    };
    const shell = new (await import('./finance-shell')).FinanceShell(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as never,
    );
    shell.activeWorkspace = { id: 'old-space' } as never;
    const first = shell.ngOnInit();
    await Promise.resolve();
    shell.activeWorkspace = { id: 'new-space' } as never;
    shell.ngOnChanges({ activeWorkspace: { previousValue: { id: 'old-space' }, currentValue: { id: 'new-space' } } as never });
    await Promise.resolve();
    await Promise.resolve();
    await api.financeOverview.mock.results[1].value;
    finishOld({ period: '2026-10', marker: 'old-space' } as never);
    await first;

    expect(api.selectWorkspace).toHaveBeenLastCalledWith('new-space');
    expect(shell.overview?.period).toBe('2026-11');
    expect(shell.overview).toMatchObject({ marker: 'new-space' });
  });

  it('navigates from Más to accounts without changing the selected period', async () => {
    const shell = new (await import('./finance-shell')).FinanceShell(
      { financeOverview: vi.fn() } as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    shell.period = '2026-11';
    shell.navigate('more');
    expect(shell.section).toBe('more');
    shell.navigateMore('accounts');
    expect(shell.section).toBe('accounts');
    expect(shell.period).toBe('2026-11');
  });

  it('clears the active account action without clearing its draft', () => {
    const accounts = new FinanceAccounts(
      {} as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    accounts.accountDraft.name = 'Caja del viaje';
    accounts.openForm('create');
    expect(accounts.activeForm).toBe('create');
    accounts.closeForm();
    expect(accounts.activeForm).toBe('');
    expect(accounts.accountDraft.name).toBe('Caja del viaje');
  });

  it('keeps an account action open while a save is in progress', () => {
    const accounts = new FinanceAccounts(
      {} as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    accounts.openForm('create');
    accounts.saving = true;
    accounts.closeForm();
    expect(accounts.activeForm).toBe('create');
  });

  it('opens card edit, statement review, and payment actions on demand', () => {
    const cards = new FinanceCards(
      {} as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    cards.editCard({
      id: 'card-1',
      name: 'Visa',
      bankName: 'Banco',
      closingDay: 10,
      dueDay: 20,
      isArchived: false,
      version: 1,
    } as never);
    expect(cards.activeForm).toBe('edit');
    cards.openForm('statement');
    expect(cards.activeForm).toBe('statement');
    cards.openForm('payment');
    expect(cards.activeForm).toBe('payment');
  });

  it('closes a confirmed statement before switching cards but retains the form on failure', async () => {
    const api = {
      confirmStatement: vi.fn().mockResolvedValue(undefined),
      statement: vi.fn().mockImplementation((cardId: string) =>
        Promise.resolve({
          id: `statement-${cardId}`,
          cardId,
          period: '2026-10',
          currency: 'ARS',
          version: 1,
          state: cardId === 'card-1' ? 'Confirmed' : 'Draft',
          dueDate: null,
          actualTotal: null,
          expectedAmount: '0.00',
          carryAmount: '0.00',
          pendingAmount: '0.00',
          creditBalance: '0.00',
          installments: [],
          adjustments: [],
          payments: [],
        }),
      ),
      purchases: vi.fn().mockResolvedValue([]),
    };
    const cards = new FinanceCards(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    cards.overview = { cards: [{ id: 'card-1' }, { id: 'card-2' }] } as never;
    cards.selectedCardId = 'card-1';
    cards.statement = {
      id: 'statement-card-1',
      cardId: 'card-1',
      period: '2026-10',
      currency: 'ARS',
      version: 1,
      state: 'Draft',
      dueDate: null,
      actualTotal: null,
      expectedAmount: '0.00',
      carryAmount: '0.00',
      pendingAmount: '0.00',
      creditBalance: '0.00',
      installments: [],
      adjustments: [],
      payments: [],
    };
    cards.activeForm = 'statement';
    cards.refreshOverview = vi.fn().mockResolvedValue(undefined);

    await cards.confirmStatement();
    cards.selectCard('card-2');
    await cards.loadStatement();

    expect(cards.activeForm).toBe('');
    expect(cards.statement?.state).toBe('Draft');
    cards.openForm('statement');
    expect(cards.activeForm).toBe('statement');

    cards.activeForm = 'statement';
    cards.statement = { ...cards.statement!, state: 'Draft' };
    cards.statementDraft = {
      currency: 'ARS',
      dueDate: '2026-11-02',
      actualTotal: '123.45',
      adjustments: [
        { id: 'adjustment-1', description: 'Ajuste', category: 'Otros', amount: '1.25' },
      ],
    };
    api.confirmStatement.mockRejectedValueOnce(new Error('Conflicto'));
    await cards.confirmStatement();

    expect(cards.activeForm).toBe('statement');
    expect(cards.statement?.state).toBe('Draft');
    expect(cards.statementDraft).toEqual({
      currency: 'ARS',
      dueDate: '2026-11-02',
      actualTotal: '123.45',
      adjustments: [
        { id: 'adjustment-1', description: 'Ajuste', category: 'Otros', amount: '1.25' },
      ],
    });
    expect(cards.error).toBe('Conflicto');
  });

  it('opens usable finance actions for an authenticated user before onboarding is complete', async () => {
    const api = {
      session: vi.fn().mockResolvedValue({
        authenticated: true,
        displayName: 'Ana',
        googleConfigured: false,
        developmentLoginEnabled: false,
        developmentUsers: [],
        csrfToken: 'csrf',
      }),
      me: vi.fn().mockResolvedValue({ id: 'user', displayName: 'Ana', email: null, emailVerified: false, personalWorkspaceId: 'personal' }),
      workspaces: vi.fn().mockResolvedValue([{ id: 'personal', name: 'Personal', ownerDisplayName: 'Ana', kind: 'Personal', role: 'Owner', isSharedPersonalRead: false, isArchived: false, version: 1, permissions: { read: true, readGroups: true, recordFinance: true, manageResources: true, correctHistory: true, manageMembers: true, managePrivacy: true } }]),
      selectWorkspace: vi.fn(),
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
      workspace: vi.fn().mockResolvedValue({ id: 'personal', permissions: { read: true, readGroups: true, recordFinance: true, manageResources: true, correctHistory: true, manageMembers: true, managePrivacy: true }, isArchived: false }),
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
    shell.activeWorkspace = { id: 'personal' } as never;

    await shell.refresh();

    expect(api.financeOverview).toHaveBeenCalledWith('2026-11');
    expect(shell.overview?.period).toBe('2026-11');
  });

  it('formats valid ISO months and suppresses empty or malformed captions safely', async () => {
    const shell = new (await import('./finance-shell')).FinanceShell(
      {} as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );

    shell.period = '2026-01';
    expect(shell.periodLabel).toBe('enero 2026');
    shell.period = '2026-12';
    expect(shell.periodLabel).toBe('diciembre 2026');
    shell.period = '0099-01';
    expect(shell.periodLabel).toBe('enero 0099');

    for (const invalid of ['', '2026-00', '2026-13', '2026/01', 'abc']) {
      shell.period = invalid;
      expect(() => shell.periodLabel).not.toThrow();
      expect(shell.periodLabel).toBe('');
    }
  });

  it('rejects invalid periods before requesting finance data and preserves the applied overview', async () => {
    const api = {
      workspace: vi.fn().mockResolvedValue({ id: 'personal' }),
      financeOverview: vi.fn(),
    };
    const shell = new (await import('./finance-shell')).FinanceShell(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    const appliedOverview = { period: '2026-10', marker: 'last-good-response' };
    shell.activeWorkspace = { id: 'personal' } as never;
    shell.overview = appliedOverview as never;

    for (const invalid of ['', '2026-00', '2026-13', '2026/01', 'abc']) {
      shell.period = invalid;
      await shell.changePeriod();
      expect(shell.error).toMatch(/período válido/i);
      expect(shell.overview).toBe(appliedOverview);
      expect(api.workspace).not.toHaveBeenCalled();
      expect(api.financeOverview).not.toHaveBeenCalled();
    }
  });

  it('keeps an open account action and reuses its request ID after a failed save', async () => {
    const api = {
      createAccount: vi
        .fn()
        .mockRejectedValueOnce(new Error('Versión desactualizada'))
        .mockResolvedValue({ id: 'account-created' }),
    };
    const refresh = vi.fn().mockResolvedValue(undefined);
    const accounts = new FinanceAccounts(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    accounts.overview = { accounts: [] } as never;
    accounts.refreshOverview = refresh;
    accounts.accountDraft = {
      name: 'Caja ahorro',
      bankName: 'Banco',
      currency: 'ARS',
      openingBalance: '15200,40',
      openingDate: '2026-10-24',
    };
    accounts.openForm('create');

    await accounts.saveAccount();

    expect(accounts.activeForm).toBe('create');
    expect(accounts.accountDraft).toEqual({
      name: 'Caja ahorro',
      bankName: 'Banco',
      currency: 'ARS',
      openingBalance: '15200,40',
      openingDate: '2026-10-24',
    });
    expect(accounts.error).toBe('Versión desactualizada');
    expect(refresh).not.toHaveBeenCalled();
    expect(api.createAccount).toHaveBeenCalledWith(
      expect.objectContaining({ openingBalance: '15200.40' }),
    );

    const firstRequestId = api.createAccount.mock.calls[0][0].id;
    await accounts.saveAccount();

    expect(api.createAccount.mock.calls[1][0].id).toBe(firstRequestId);
    expect(accounts.activeForm).toBe('');
    expect(accounts.accountDraft.name).toBe('');
    expect(refresh).toHaveBeenCalledOnce();
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

  it('keeps monthly-rule controls locked until the saved rule finishes hydrating', async () => {
    let finishSave!: () => void;
    const savePending = new Promise<void>((resolve) => (finishSave = resolve));
    let finishRefresh!: () => void;
    const refreshPending = new Promise<void>((resolve) => (finishRefresh = resolve));
    let announceRefresh!: () => void;
    const refreshStarted = new Promise<void>((resolve) => (announceRefresh = resolve));
    const participants = [
      { id: 'person-a', name: 'Ana', isSelf: true },
      { id: 'person-b', name: 'Beto', isSelf: false },
    ];
    const api = {
      saveRule: vi.fn().mockReturnValue(savePending),
      receivables: vi.fn().mockResolvedValue([]),
    };
    const sharing = new FinanceSharing(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    sharing.period = '2026-11';
    sharing.selectedGroupId = 'group-1';
    sharing.ruleMode = 'Percentage';
    sharing.ruleWeights = { 'person-a': '50', 'person-b': '50' };
    sharing.overview = {
      groups: [
        {
          id: 'group-1',
          name: 'Viaje',
          version: 1,
          participants,
          rule: null,
        },
      ],
    } as never;
    sharing.refreshOverview = vi.fn(async () => {
      sharing.overview = {
        groups: [
          {
            id: 'group-1',
            name: 'Viaje',
            version: 2,
            participants,
            rule: {
              period: '2026-11',
              version: 1,
              mode: 'Percentage',
              basisCurrency: 'ARS',
              weights: [
                { id: 'person-a', value: '50.00' },
                { id: 'person-b', value: '50.00' },
              ],
              confirmed: false,
            },
          },
        ],
      } as never;
      sharing.ngOnChanges();
      announceRefresh();
      await refreshPending;
    });

    const saving = sharing.saveRule();
    expect(sharing.saving).toBe(true);
    finishSave();
    await refreshStarted;

    expect(sharing.saving).toBe(true);
    expect(sharing.ruleWeights).toEqual({ 'person-a': '50.00', 'person-b': '50.00' });
    expect(api.receivables).toHaveBeenCalledWith('group-1', '2026-11');

    finishRefresh();
    await saving;
    expect(sharing.saving).toBe(false);
    expect(sharing.notice).toBe('Reparto mensual guardado.');
  });

  it('requires real FX transfer amounts and uses equality only for matching account currencies', async () => {
    const api = { transfer: vi.fn().mockResolvedValue(undefined) };
    const operations = new FinanceOperations(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    operations.overview = {
      accounts: [
        { id: 'ars-out', currency: 'ARS', isArchived: false },
        { id: 'usd-in', currency: 'USD', isArchived: false },
        { id: 'ars-in', currency: 'ARS', isArchived: false },
      ],
    } as never;
    operations.refreshOverview = vi.fn().mockResolvedValue(undefined);
    operations.transferDraft = {
      fromAccountId: 'ars-out',
      toAccountId: 'usd-in',
      date: '2026-10-24',
      amount: '100',
      destinationAmount: '',
    };

    await operations.transfer();

    expect(api.transfer).not.toHaveBeenCalled();
    expect(operations.error).toContain('importe real');
    expect(operations.transferDraft.destinationAmount).toBe('');

    operations.transferDraft.destinationAmount = '12,50';
    await operations.transfer();
    expect(api.transfer).toHaveBeenNthCalledWith(
      1,
      expect.objectContaining({ amount: '100', destinationAmount: '12.50' }),
    );

    operations.transferDraft = {
      fromAccountId: 'ars-out',
      toAccountId: 'ars-in',
      date: '2026-10-24',
      amount: '35,00',
      destinationAmount: '',
    };
    await operations.transfer();
    expect(api.transfer).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({ amount: '35.00', destinationAmount: '35.00' }),
    );
  });

  it('requires the real card debt amount for FX payments and defaults only for same-currency accounts', async () => {
    const statement = {
      id: 'statement-1',
      cardId: 'card-1',
      period: '2026-10',
      currency: 'USD',
      version: 2,
      state: 'Confirmed',
      dueDate: '2026-11-10',
      actualTotal: '100',
      expectedAmount: '100',
      carryAmount: '0',
      pendingAmount: '100',
      creditBalance: '0',
      installments: [],
      adjustments: [],
      payments: [],
    };
    const api = {
      payCard: vi.fn().mockResolvedValue(undefined),
      financeOverview: vi.fn().mockResolvedValue({}),
      statement: vi.fn().mockResolvedValue(statement),
    };
    const cards = new FinanceCards(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    cards.selectedCardId = 'card-1';
    cards.statement = statement as never;
    cards.overview = {
      accounts: [
        { id: 'account-ars', currency: 'ARS', isArchived: false },
        { id: 'account-usd', currency: 'USD', isArchived: false },
      ],
    } as never;
    cards.refreshOverview = vi.fn().mockResolvedValue(undefined);
    cards.cardPayment = {
      accountId: 'account-ars',
      date: '2026-10-24',
      amount: '5000',
      appliedAmount: '',
      currency: 'USD',
    };

    await cards.payStatement();

    expect(api.payCard).not.toHaveBeenCalled();
    expect(cards.error).toContain('importe real');
    expect(cards.cardPayment.amount).toBe('5000');
    expect(cards.cardPayment.appliedAmount).toBe('');

    cards.cardPayment.appliedAmount = '4,25';
    await cards.payStatement();
    expect(api.payCard).toHaveBeenNthCalledWith(
      1,
      'card-1',
      expect.objectContaining({ amount: '5000', currency: 'USD', appliedAmount: '4.25' }),
    );

    cards.cardPayment = {
      accountId: 'account-usd',
      date: '2026-10-24',
      amount: '8.50',
      appliedAmount: '',
      currency: 'USD',
    };
    await cards.payStatement();
    expect(api.payCard).toHaveBeenNthCalledWith(
      2,
      'card-1',
      expect.objectContaining({ amount: '8.50', currency: 'USD', appliedAmount: '8.50' }),
    );
  });

  it('requires an explicit FX collection application amount and defaults only for same currency', async () => {
    const row = {
      groupId: 'group-1',
      sourceId: 'installment-1',
      purchaseId: 'purchase-1',
      period: '2026-10',
      description: 'Compra',
      currency: 'USD',
      participantId: 'person-b',
      participantName: 'Beto',
      isSelf: false,
      amount: '20',
      collectedAmount: '0',
      pendingAmount: '20',
      isEstimated: false,
      ruleVersion: 1,
      monthConfirmed: true,
      status: 'Pending',
    };
    const group = {
      id: 'group-1',
      name: 'Viaje',
      version: 1,
      participants: [{ id: 'person-b', name: 'Beto', isSelf: false }],
      rule: null,
    };
    const api = {
      collect: vi.fn().mockResolvedValue(undefined),
      receivables: vi.fn().mockResolvedValue([row]),
    };
    const sharing = new FinanceSharing(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    sharing.overview = {
      accounts: [
        { id: 'account-ars', currency: 'ARS', isArchived: false },
        { id: 'account-usd', currency: 'USD', isArchived: false },
      ],
      groups: [group],
    } as never;
    sharing.selectedGroupId = 'group-1';
    sharing.groupReceivables = [row] as never;
    sharing.refreshOverview = vi.fn().mockResolvedValue(undefined);
    sharing.collection = {
      participantId: 'person-b',
      sourceId: 'installment-1',
      accountId: 'account-ars',
      amount: '1200',
      appliedAmount: '',
      date: '2026-10-24',
    };

    await sharing.collect(row as never);

    expect(api.collect).not.toHaveBeenCalled();
    expect(sharing.error).toContain('importe real');
    expect(sharing.collection.amount).toBe('1200');
    expect(sharing.collection.appliedAmount).toBe('');

    sharing.collection.appliedAmount = '8,25';
    await sharing.collect(row as never);
    expect(api.collect).toHaveBeenCalledWith(
      'group-1',
      expect.objectContaining({ amount: '1200', appliedAmount: '8.25' }),
    );

    sharing.collection = {
      participantId: 'person-b',
      sourceId: 'installment-1',
      accountId: 'account-usd',
      amount: '10.00',
      appliedAmount: '',
      date: '2026-10-24',
    };
    await sharing.collect(row as never);
    expect(api.collect).toHaveBeenNthCalledWith(
      2,
      'group-1',
      expect.objectContaining({ amount: '10.00', appliedAmount: '10.00' }),
    );
  });

  it('keeps account creation IDs on identical retries, rotates edited payloads, then resets after success', async () => {
    const api = {
      createAccount: vi
        .fn()
        .mockRejectedValueOnce(new Error('Network error'))
        .mockRejectedValueOnce(new Error('Network error'))
        .mockRejectedValueOnce(new Error('Network error'))
        .mockResolvedValue({ id: 'saved-account' }),
    };
    const accounts = new FinanceAccounts(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    accounts.overview = { accounts: [] } as never;
    accounts.refreshOverview = vi.fn().mockResolvedValue(undefined);
    accounts.accountDraft = {
      name: 'Caja',
      bankName: 'Banco',
      currency: 'ARS',
      openingBalance: '1000,00',
      openingDate: '2026-10-24',
    };

    await accounts.saveAccount();
    const firstId = api.createAccount.mock.calls[0][0].id;
    await accounts.saveAccount();
    expect(api.createAccount.mock.calls[1][0].id).toBe(firstId);

    accounts.accountDraft.name = 'Caja principal';
    await accounts.saveAccount();
    const editedId = api.createAccount.mock.calls[2][0].id;
    expect(editedId).not.toBe(firstId);

    await accounts.saveAccount();
    expect(api.createAccount.mock.calls[3][0].id).toBe(editedId);
    accounts.accountDraft = {
      name: 'Caja principal',
      bankName: 'Banco',
      currency: 'ARS',
      openingBalance: '1000,00',
      openingDate: '2026-10-24',
    };
    await accounts.saveAccount();
    expect(api.createAccount.mock.calls[4][0].id).not.toBe(editedId);
  });

  it('keeps card creation IDs on identical retries and rotates them when the failed draft changes', async () => {
    const api = {
      createCard: vi
        .fn()
        .mockRejectedValueOnce(new Error('Network error'))
        .mockRejectedValueOnce(new Error('Network error'))
        .mockResolvedValue({ id: 'saved-card' }),
    };
    const cards = new FinanceCards(
      api as unknown as ApiService,
      { markForCheck: vi.fn() } as unknown as import('@angular/core').ChangeDetectorRef,
    );
    cards.overview = { cards: [] } as never;
    cards.refreshOverview = vi.fn().mockResolvedValue(undefined);
    cards.cardDraft = {
      name: 'Visa',
      bankName: 'Banco',
      closingDay: '10',
      dueDay: '20',
      statementDebtArs: '5000,00',
      statementDebtUsd: '0',
      asOfDate: '2026-10-24',
    };

    await cards.saveCard();
    const firstId = api.createCard.mock.calls[0][0].id;
    await cards.saveCard();
    expect(api.createCard.mock.calls[1][0].id).toBe(firstId);

    cards.cardDraft.name = 'Visa principal';
    await cards.saveCard();
    expect(api.createCard.mock.calls[2][0].id).not.toBe(firstId);
  });
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail; });
  return { promise, resolve, reject };
}
