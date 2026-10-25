import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../core/api.service';
import { FinanceProjection } from './projection';

const currency = (currency: 'ARS' | 'USD') => ({ currency, cashBalance: currency === 'ARS' ? '25000.75' : '10.20', availableCash: currency === 'ARS' ? '24000.75' : '10.20', restrictedCash: '0.00', reservedNominal: '1000.00', backingDeficit: '0.00', freeBalance: currency === 'ARS' ? '23000.75' : '10.20', minimumFreeBalance: currency === 'ARS' ? '-250.00' : '9.00', firstShortfallDate: currency === 'ARS' ? '2026-10-31' : null, dailyBalances: [{ date: '2026-10-25', cashBalance: '25000.75', availableCash: '24000.75', restrictedCash: '0.00', reservedNominal: '1000.00', freeBalance: '23000.75' }] });

describe('projection and exchange views', () => {
  it('keeps ARS and USD series separate and reports the minimum and first shortfall from the same currency series', () => {
    const screen = new FinanceProjection({} as ApiService, { markForCheck: vi.fn() } as never);
    screen.data = { workspaceId: 'w', workspaceName: 'Casa', asOf: '2026-10-25', currencies: [currency('ARS'), currency('USD')], rows: [], alerts: [] };
    expect(screen.currencies.map(x => [x.currency, x.cashBalance])).toEqual([['ARS', '25000.75'], ['USD', '10.20']]);
    expect(screen.shortfallExplanation(screen.currencies[0])).toContain('31 oct 2026');
    expect(screen.shortfallExplanation(screen.currencies[1])).toContain('no aparece un primer faltante');
    expect(screen.dateBalance(screen.currencies[0])).toBe('23000.75');
  });

  it('keeps undated and overdue rows available in date detail and identifies information credits as noncash', () => {
    const screen = new FinanceProjection({} as ApiService, { markForCheck: vi.fn() } as never);
    screen.selectedDate = '2026-10-25'; screen.data = { workspaceId: 'w', workspaceName: 'Casa', asOf: '2026-10-25', currencies: [], alerts: [], rows: [
      { sourceKind: 'FinanceOperation', sourceId: '1', componentId: null, date: '2026-10-02', dateCertainty: 'Overdue', currency: 'ARS', amount: '10.00', direction: 'Outgoing', certainty: 'Confirmed', accountId: null, description: 'Servicio vencido', summaryId: null, warnings: ['Vencimiento atrasado; se evalúa desde hoy.'] },
      { sourceKind: 'CardCredit', sourceId: '2', componentId: null, date: null, dateCertainty: 'Unknown', currency: 'USD', amount: '4.00', direction: 'Information', certainty: 'Information', accountId: null, description: 'Crédito informativo', summaryId: null, warnings: ['No es efectivo.'] },
      { sourceKind: 'FinanceOperation', sourceId: '3', componentId: null, date: '2026-10-30', dateCertainty: 'Confirmed', currency: 'USD', amount: '2.00', direction: 'Outgoing', certainty: 'Confirmed', accountId: null, description: 'Otro día', summaryId: null, warnings: [] },
    ] };
    expect(screen.filteredRows().map(x => x.sourceId)).toEqual(['1', '2']);
    expect(screen.includedReason(screen.rows[1])).toContain('no suma efectivo');
    expect(screen.rowKey(screen.rows[0])).toBe('FinanceOperation|1||ARS');
  });

  it('does not expose saved-scenario controls to readers while allowing temporary calculations', () => {
    const screen = new FinanceProjection({} as ApiService, { markForCheck: vi.fn() } as never);
    screen.permissions = { read: true, readGroups: false, recordFinance: false, manageResources: false, correctHistory: false, manageMembers: false, managePrivacy: false };
    screen.certainty = 'Estimated'; screen.assumptions = [{ id: 'a', kind: 'Income', date: '2026-11-01', currency: 'ARS', amount: '500.00', description: 'Bono', sourceId: null }];
    screen.openSaveScenario();
    expect(screen.dialog).toBe(''); expect(screen.hasPendingDraft()).toBe(true);
  });

  it('loads a saved scenario into one ephemeral copy and does not ask the API to apply it twice', async () => {
    const saved = { id: 'assumption-1', kind: 'Expense' as const, date: '2026-11-04', currency: 'USD' as const, amount: '120.15', description: 'Seguro', sourceId: null, sourceKind: null, componentId: null };
    const calculateProjection = vi.fn().mockResolvedValue({ workspaceId: 'w', workspaceName: 'Casa', asOf: '2026-10-25', currencies: [], rows: [], alerts: [] });
    const api = { calculateProjection, projectionScenarios: vi.fn().mockResolvedValue([{ id: 'scenario-1', name: 'Mudanza', version: 2, assumptions: [saved] }]) };
    const screen = new FinanceProjection(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'w'; screen.scenarios = [{ id: 'scenario-1', name: 'Mudanza', version: 2, assumptions: [saved] }];
    await screen.chooseScenario('scenario-1');
    expect(screen.assumptions).toEqual([saved]);
    expect(calculateProjection).toHaveBeenCalledWith(expect.objectContaining({ savedScenarioId: null, assumptions: [saved] }));
  });

  it('selects complete source identity for an ephemeral amount adjustment without exposing IDs as labels', async () => {
    const calculateProjection = vi.fn().mockResolvedValue({ workspaceId: 'w', workspaceName: 'Casa', asOf: '2026-10-25', currencies: [], rows: [], alerts: [] });
    const api = { calculateProjection };
    const screen = new FinanceProjection(api as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'w'; screen.data = { workspaceId: 'w', workspaceName: 'Casa', asOf: '2026-10-25', currencies: [], alerts: [], rows: [{ sourceKind: 'CardObligation', sourceId: 'source-guid', componentId: 'Installment:ARS', date: '2026-11-10', dateCertainty: 'Confirmed', currency: 'ARS', amount: '1000.00', direction: 'Outgoing', certainty: 'Confirmed', accountId: null, description: 'Cuota del lavarropas', summaryId: null, warnings: [] }] };
    const sourceRow = screen.rows[0]; screen.through = '2027-01-01'; screen.assumptionDraft = { kind: 'AmountChange', sourceKey: 'CardObligation|source-guid|Installment:ARS|ARS', date: '2026-10-25', currency: 'ARS', amount: '-25.10', description: 'Ajuste informado' };
    await screen.addAssumption();
    expect(screen.assumptions[0]).toMatchObject({ kind: 'AmountChange', sourceId: 'source-guid', sourceKind: 'CardObligation', componentId: 'Installment:ARS', currency: 'ARS', amount: '-25.10' });
    expect(screen.sourceLabel(sourceRow)).toBe('Obligación de tarjeta');
  });

  it('does not restore an old workspace quote response after the context is invalidated', async () => {
    let finish!: (value: never) => void;
    const result = new Promise<never>(resolve => { finish = resolve; });
    const screen = new FinanceProjection({ exchangeRates: vi.fn(() => result) } as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'old-space'; const load = screen.loadRates();
    (screen as unknown as { invalidateContext: () => void }).invalidateContext.call(screen);
    finish({ status: 'Disponible', stale: false, snapshots: [{ id: 'old-rate' }] } as never); await load;
    expect(screen.rates).toBeNull();
  });

  it('shows the latest quote per house and preserves a historical snapshot selected by a savings goal', () => {
    const screen = new FinanceProjection({} as ApiService, { markForCheck: vi.fn() } as never);
    screen.rates = { status: 'Disponible', stale: false, error: null, fetchedAt: '2026-10-25T12:00:00Z', snapshots: [
      { id: 'old', currency: 'USD', house: 'Oficial', name: 'Oficial', buy: '1100.00', sell: '1150.00', providerUpdatedAt: '2026-10-24T12:00:00Z', fetchedAt: '2026-10-24T12:05:00Z' },
      { id: 'new', currency: 'USD', house: 'Oficial', name: 'Oficial', buy: '1200.00', sell: '1250.00', providerUpdatedAt: '2026-10-25T12:00:00Z', fetchedAt: '2026-10-25T12:05:00Z' },
    ] };
    screen.savedRateSelections = [{ snapshotId: 'old', goalName: 'Viaje' }];
    expect(screen.latestRates.map(x => x.id)).toEqual(['new', 'old']);
    expect(screen.savedRateGoals(screen.rates.snapshots[0])).toBe('Viaje');
  });
});
