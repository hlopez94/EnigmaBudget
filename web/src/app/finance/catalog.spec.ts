import '@angular/compiler';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../core/api.service';
import { FinanceCatalogScreen } from './catalog';

const catalogData = {
  categories: [
    { itemId: 'cat-1', name: 'Sin clasificar', kind: 'Expense' as const, isArchived: false, isDefault: true, version: 1 },
    { itemId: 'cat-2', name: 'Supermercado', kind: 'Expense' as const, isArchived: true, isDefault: false, version: 2 },
  ],
  merchants: [{ merchantId: 'merchant-1', name: 'Almacén', isArchived: false, version: 1 }],
  incomeSources: [{ incomeSourceId: 'source-1', name: 'Sueldo', kind: 'Salary' as const, isArchived: false, version: 1 }],
};
const permissions = { read: true, readGroups: false, recordFinance: false, manageResources: false, correctHistory: false, manageMembers: false, managePrivacy: false };

describe('catalog screen', () => {
  it('shows archived items for history but only offers active categories for selection', async () => {
    const screen = new FinanceCatalogScreen({ financeCatalog: vi.fn().mockResolvedValue(catalogData) } as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'space-1';
    screen.permissions = permissions;
    await screen.load();

    expect(screen.catalog.categories.map(item => item.name)).toEqual(['Sin clasificar', 'Supermercado']);
    expect(screen.selectableCategories.map(item => item.itemId)).toEqual(['cat-1']);
    expect(screen.canManage).toBe(false);
  });

  it('retains a failed source draft and request identity for an identical retry', async () => {
    const createIncomeSource = vi.fn().mockRejectedValue(new Error('Sin conexión'));
    const screen = new FinanceCatalogScreen({ financeCatalog: vi.fn().mockResolvedValue(catalogData), createIncomeSource } as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'space-1';
    screen.permissions = { ...permissions, manageResources: true };
    screen.view = 'sources';
    await screen.load();
    screen.newName = 'Bono anual';
    screen.newKind = 'AnnualBonus';

    await screen.createItem();
    const firstId = createIncomeSource.mock.calls[0][0].id;
    await screen.createItem();

    expect(screen.newName).toBe('Bono anual');
    expect(createIncomeSource.mock.calls[1][0].id).toBe(firstId);
  });

  it('clears catalog queries and drafts when the workspace context changes', async () => {
    const financeCatalog = vi.fn().mockResolvedValue(catalogData);
    const screen = new FinanceCatalogScreen({ financeCatalog } as unknown as ApiService, { markForCheck: vi.fn() } as never);
    screen.workspaceId = 'old-space';
    screen.permissions = permissions;
    await screen.load();
    screen.newName = 'Pendiente';
    screen.workspaceId = 'new-space';
    screen.ngOnChanges({ workspaceId: { previousValue: 'old-space', currentValue: 'new-space', firstChange: false, isFirstChange: () => false } } as never);

    expect(screen.catalog.categories).toEqual([]);
    expect(screen.newName).toBe('');
    expect(financeCatalog).toHaveBeenCalledTimes(2);
  });
});
