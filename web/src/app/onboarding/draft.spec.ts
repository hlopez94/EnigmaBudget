import { afterEach, describe, expect, it, vi } from 'vitest';
import { AccountInput, CardInput, OnboardingState } from '../core/api.models';
import { ApiProblem, ApiService } from '../core/api.service';
import { accountDraft, cardDebtIsValid, normalizeDecimal, openingTotals } from './draft';
import { OnboardingDraftController } from './draft-controller';

const account = (name: string, currency: 'ARS' | 'USD', openingBalance: string): AccountInput => ({ id: name, name, bankName: 'Banco', currency, openingBalance, openingDate: '2026-10-24' });
const state = (version: number, accounts: AccountInput[] = [], cards: CardInput[] = [], completed = false): OnboardingState => ({ version, completed, accounts, cards });

afterEach(() => vi.unstubAllGlobals());

describe('onboarding draft rules', () => {
  it('preserves the account amount as the exact decimal string', () => {
    const value = accountDraft({ name: 'Caja', bankName: 'Banco', currency: 'ARS', openingBalance: '00123.40', openingDate: '2026-10-24' }, 'account-1');
    expect(value.openingBalance).toBe('00123.40');
  });
  it('normalizes a decimal comma without converting through a JavaScript number', () => {
    expect(normalizeDecimal('00123,40')).toBe('00123.40');
  });
  it('rejects negative statement debt', () => {
    const card: CardInput = { id: 'card-1', name: 'Visa', bankName: 'Banco', closingDay: null, dueDay: null, statementDebtArs: '-0.01', statementDebtUsd: '0', asOfDate: '2026-10-24' };
    expect(cardDebtIsValid(card)).toBe(false);
  });
  it('keeps ARS and USD account totals separate using exact minor units', () => {
    expect(openingTotals([account('Pesos','ARS','100.10'), account('Dólares','USD','20.25'), account('Efectivo','ARS','-0.15')])).toEqual({ ARS: '99.95', USD: '20.25' });
  });
  it('restores a partially completed draft from the API state', async () => {
    const saved = state(3, [account('Caja','ARS','10.50')]);
    const api = { loadOnboarding: vi.fn().mockResolvedValue(saved), saveDraft: vi.fn(), completeDraft: vi.fn() };
    const controller = new OnboardingDraftController(api);
    await controller.restore();
    expect(controller.version).toBe(3);
    expect(controller.accounts).toEqual(saved.accounts);
  });
  it('keeps edits entered while a save snapshot is pending', async () => {
    let resolveSave!: (value: OnboardingState) => void;
    const saveResponse = new Promise<OnboardingState>(resolve => { resolveSave = resolve; });
    const api = { loadOnboarding: vi.fn(), saveDraft: vi.fn().mockReturnValue(saveResponse), completeDraft: vi.fn() };
    const controller = new OnboardingDraftController(api);
    controller.accounts = [account('Antes de guardar','ARS','80.00')];

    const pendingSave = controller.save();
    expect(api.saveDraft.mock.calls[0][0].accounts[0].name).toBe('Antes de guardar');
    controller.accounts[0].name = 'Edición posterior';
    resolveSave(state(3, [account('Antes de guardar','ARS','80.00')]));
    await pendingSave;

    expect(controller.accounts[0].name).toBe('Edición posterior');
    expect(controller.version).toBe(3);
  });
  it('refreshes a stale version and can explicitly save local edits over the latest draft', async () => {
    const latest = state(4, [account('Versión de otro dispositivo','ARS','90.00')]);
    const localSaved = state(5, [account('Mi edición local','ARS','80.00')]);
    const api = { loadOnboarding: vi.fn().mockResolvedValue(latest), saveDraft: vi.fn()
      .mockRejectedValueOnce(new ApiProblem(409, 'El borrador cambió.'))
      .mockResolvedValueOnce(localSaved), completeDraft: vi.fn() };
    const controller = new OnboardingDraftController(api);
    controller.accounts = [account('Mi edición local','ARS','80.00')];

    await controller.save();
    expect(controller.accounts[0].name).toBe('Mi edición local');
    await controller.refreshLatest();
    expect(controller.latestState?.version).toBe(4);
    expect(controller.accounts[0].name).toBe('Mi edición local');
    await controller.saveLocalOverLatest();

    expect(api.saveDraft.mock.calls[1][0].version).toBe(4);
    expect(api.saveDraft.mock.calls[1][0].accounts[0].name).toBe('Mi edición local');
    expect(controller.version).toBe(5);
    expect(controller.accounts[0].name).toBe('Mi edición local');
    expect(controller.conflict).toBe(false);
  });
  it('replaces local values only after choosing the refreshed server draft', async () => {
    const latest = state(7, [account('Guardada en el servidor','USD','32.10')]);
    const api = { loadOnboarding: vi.fn().mockResolvedValue(latest), saveDraft: vi.fn(), completeDraft: vi.fn() };
    const controller = new OnboardingDraftController(api);
    controller.accounts = [account('Edición local','ARS','80.00')];
    controller.conflict = true;

    await controller.refreshLatest();
    expect(controller.accounts[0].name).toBe('Edición local');
    const applied = controller.useLatestVersion();

    expect(applied?.version).toBe(7);
    expect(controller.version).toBe(7);
    expect(controller.accounts).toEqual(latest.accounts);
  });
  it('uses a newly completed draft instead of retrying a local overwrite', async () => {
    const finished = state(6, [account('Guardada','ARS','2.00')], [], true);
    const api = { loadOnboarding: vi.fn().mockResolvedValue(finished), saveDraft: vi.fn(), completeDraft: vi.fn() };
    const controller = new OnboardingDraftController(api);
    controller.conflict = true;
    await controller.refreshLatest();

    expect(controller.latestState?.completed).toBe(true);
    expect(await controller.saveLocalOverLatest()).toBe(false);
    const applied = controller.useLatestVersion();
    expect(applied?.completed).toBe(true);
    expect(controller.accounts).toEqual(finished.accounts);
    expect(api.saveDraft).not.toHaveBeenCalled();
  });
  it('shows a stale-save conflict and preserves local edits', async () => {
    const api = { loadOnboarding: vi.fn(), saveDraft: vi.fn().mockRejectedValue(new ApiProblem(409, 'El borrador cambió. Actualizá la pantalla antes de guardar.')), completeDraft: vi.fn() };
    const controller = new OnboardingDraftController(api);
    controller.accounts = [account('Cuenta editada','ARS','80.00')];
    await controller.save();
    expect(controller.accounts[0].name).toBe('Cuenta editada');
    expect(controller.error).toMatch(/cambió/i);
  });
  it('retains the form after a confirmation error so the user can retry', async () => {
    const saved = state(3, [account('Cuenta','ARS','80.00')]);
    const api = { loadOnboarding: vi.fn(), saveDraft: vi.fn().mockResolvedValue(saved), completeDraft: vi.fn().mockRejectedValue(new Error('Sin conexión')) };
    const controller = new OnboardingDraftController(api);
    controller.version = 2; controller.accounts = saved.accounts;
    await controller.confirm();
    expect(controller.completed).toBe(false);
    expect(controller.accounts).toHaveLength(1);
    expect(controller.error).toBe('Sin conexión');
  });
});

describe('API session security', () => {
  it('refreshes the session and CSRF token after development sign-in', async () => {
    const request = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ authenticated:false, displayName:null, googleConfigured:false, developmentLoginEnabled:true, csrfToken:'before-login' }), { status:200 }))
      .mockResolvedValueOnce(new Response(null, { status:204 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ authenticated:true, displayName:'Usuario de desarrollo', googleConfigured:false, developmentLoginEnabled:true, csrfToken:'after-login' }), { status:200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(state(0)), { status:200 }));
    vi.stubGlobal('fetch', request);
    const api = new ApiService();
    await api.session();
    const session = await api.signInDevelopment();
    await api.loadOnboarding();
    expect(session.csrfToken).toBe('after-login');
    expect(request.mock.calls[1][1]).toMatchObject({ credentials:'include', method:'POST', headers:{ 'Content-Type':'application/json', 'X-CSRF-TOKEN':'before-login' } });
    expect(request.mock.calls[3][1]).toMatchObject({ credentials:'include' });
  });
});
