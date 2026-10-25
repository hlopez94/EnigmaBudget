import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../app';
import { ApiService } from '../core/api.service';
import { normalizeDecimal } from '../onboarding/draft';
import { FinanceAccounts } from './accounts';
import { FinanceCards } from './cards';
import { FinanceOperations } from './operations';
import { FinanceSharing } from './sharing';

describe('finance workspace regressions', () => {
  it('reaches accounts from Más without changing the selected period or section drafts', async () => {
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

  it('opens and dismisses the account creation action without clearing its draft', () => {
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
