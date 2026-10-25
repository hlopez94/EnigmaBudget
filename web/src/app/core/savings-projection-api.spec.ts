import { describe, expect, it, vi } from 'vitest';
import { ApiService, Fetcher } from './api.service';

describe('savings and projection API contracts', () => {
  it('uses the selected workspace and CSRF-aware mutations while preserving decimal strings and null estimates', async () => {
    const calls: { url: string; init?: RequestInit }[] = [];
    const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input); calls.push({ url, init });
      if (url === '/api/auth/session') return new Response(JSON.stringify({ csrfToken: 'csrf-token' }), { status: 200 });
      return new Response(JSON.stringify({}), { status: 200 });
    }) as unknown as Fetcher;
    const api = new ApiService(fetcher); await api.session(); api.selectWorkspace('space-7');
    const goal = { id: 'g1', name: 'Viaje', targetCurrency: 'USD' as const, targetAmount: '123456789.12', desiredDate: null, monthlyContribution: null, nextContributionDate: null };
    await api.createSavingsGoal(goal);
    await api.savingsGoalDetails('g1', 3);
    await api.setSavingsGoalRate('g1', { requestId: 'r1', goalVersion: 2, mode: 'Manual', snapshotId: null, house: null, side: null, manualArsPerUsd: '1200.12345678', manualReason: 'Cotización acordada' });
    await api.exchangeRates(); await api.refreshExchangeRates({ id: 'r2' });
    await api.projection('2027-10-25', 'Confirmed');
    await api.calculateProjection({ through: '2027-10-25', scenario: 'Estimated', savedScenarioId: null, assumptions: [{ id: 'a1', kind: 'Expense', date: '2026-11-01', currency: 'ARS', amount: '0.10', description: 'Servicio', sourceId: null }] });
    await api.projectionScenarios();
    for (const { init } of calls.filter(x => x.url !== '/api/auth/session')) expect(new Headers(init?.headers).get('X-Workspace-Id')).toBe('space-7');
    for (const { init } of calls.filter(x => x.init?.method)) expect(new Headers(init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
    const create = calls.find(x => x.url === '/api/savings/goals')!;
    expect(JSON.parse(String(create.init?.body))).toEqual(goal);
    expect(calls.map(x => x.url)).toContain('/api/savings/goals/g1/details?historyPage=3');
    expect(calls.map(x => x.url)).toContain('/api/projection?through=2027-10-25&scenario=Confirmed');
    expect(JSON.parse(String(calls.find(x => x.url === '/api/projection/calculate')?.init?.body)).assumptions[0].amount).toBe('0.10');
  });

  it('routes reserve, release, apply, reverse, liquidity, goal close, and scenario writes through financeMutate', async () => {
    const calls: { url: string; init?: RequestInit }[] = [];
    const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => { const url = String(input); calls.push({ url, init }); return url === '/api/auth/session' ? new Response(JSON.stringify({ csrfToken: 'c' })) : new Response(JSON.stringify({})); }) as unknown as Fetcher;
    const api = new ApiService(fetcher); await api.session(); api.selectWorkspace('space');
    await api.updateSavingsGoal('g', { requestId: 'u', version: 1, name: 'Meta', targetCurrency: 'ARS', targetAmount: '12.00', desiredDate: null, state: 'Paused', monthlyContribution: null, nextContributionDate: null });
    await api.closeSavingsGoal('g', { requestId: 'c', version: 2, confirmRelease: true });
    await api.setAccountLiquidity('a', { requestId: 'l', version: 4, fundsAvailableFrom: null });
    await api.allocateSavings({ id: 'a1', goalId: 'g', accountId: 'a', amount: '2.00' });
    await api.releaseSavings('a1', { id: 'r', amount: '1.00' });
    await api.applySavings({ id: 'ap', allocationId: 'a1', financePaymentId: 'payment-actual', amount: '1.00' });
    await api.reverseSavingsApplication('m', { requestId: 'rv', reason: 'Corrección', confirm: true });
    await api.saveProjectionScenario({ id: 's', name: 'Escenario', assumptions: [] });
    await api.saveProjectionScenario({ id: 's', requestId: 'su', version: 1, name: 'Escenario', assumptions: [] });
    await api.deleteProjectionScenario('s', { requestId: 'sd', version: 2 });
    expect(calls.map(x => [x.init?.method, x.url])).toEqual(expect.arrayContaining([
      ['PUT', '/api/savings/goals/g'], ['POST', '/api/savings/goals/g/close'], ['PUT', '/api/savings/accounts/a/liquidity'], ['POST', '/api/savings/allocations'], ['POST', '/api/savings/allocations/a1/release'], ['POST', '/api/savings/applications'], ['POST', '/api/savings/applications/m/reverse'], ['POST', '/api/projection/scenarios'], ['PUT', '/api/projection/scenarios/s'], ['DELETE', '/api/projection/scenarios/s'],
    ]));
    expect(calls.filter(x => x.init?.method).every(x => new Headers(x.init?.headers).get('X-Workspace-Id') === 'space')).toBe(true);
  });
});
