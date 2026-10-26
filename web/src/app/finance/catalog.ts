import { ChangeDetectionStrategy, ChangeDetectorRef, Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FinanceCatalog, FinanceCatalogResource, FinanceCategoryOption, FinanceIncomeSourceOption, FinanceMerchantOption, OperationKind } from '../core/api.models';
import { ApiProblem, ApiService } from '../core/api.service';
import { ActionDialog } from './ui/action-dialog';
import { FinanceActions } from './finance-actions';

type CatalogItem = FinanceCategoryOption | FinanceMerchantOption | FinanceIncomeSourceOption;
type CatalogDraft = { name: string; kind: OperationKind | FinanceIncomeSourceOption['kind']; resource: 'categories' | 'merchants' };
@Component({ selector: 'app-catalog', standalone: true, imports: [FormsModule, ActionDialog], templateUrl: './catalog.html', styleUrl: './catalog.css', changeDetection: ChangeDetectionStrategy.OnPush })
export class FinanceCatalogScreen extends FinanceActions implements OnChanges {
  @Input() view: 'catalog' | 'sources' = 'catalog';
  catalog: FinanceCatalog = { categories: [], merchants: [], incomeSources: [] };
  private readonly catalogDraft: CatalogDraft = { name: '', kind: 'Expense', resource: 'categories' };
  private readonly sourceDraft: CatalogDraft = { name: '', kind: 'Salary', resource: 'categories' };
  get newName(): string { return this.activeDraft.name; }
  set newName(value: string) { this.activeDraft.name = value; }
  get newKind(): CatalogDraft['kind'] { return this.activeDraft.kind; }
  set newKind(value: CatalogDraft['kind']) { this.activeDraft.kind = value; }
  get newResource(): CatalogDraft['resource'] { return this.activeDraft.resource; }
  set newResource(value: CatalogDraft['resource']) { this.activeDraft.resource = value; }
  editItem: CatalogItem | null = null;
  editName = '';
  editOpen = false;
  loading = false;
  private generation = 0;
  private queryGeneration = 0;

  constructor(api: ApiService, changeDetector: ChangeDetectorRef) { super(api, changeDetector); }
  private get activeDraft(): CatalogDraft { return this.view === 'sources' ? this.sourceDraft : this.catalogDraft; }
  get canManage(): boolean { return !!this.permissions?.manageResources && !this.workspaceArchived; }
  get selectableCategories(): FinanceCategoryOption[] { return this.catalog.categories.filter(item => !item.isArchived); }
  get categories(): FinanceCategoryOption[] { return this.catalog.categories; }
  get merchants(): FinanceMerchantOption[] { return this.catalog.merchants; }
  get sources(): FinanceIncomeSourceOption[] { return this.catalog.incomeSources; }
  sourceKindLabel(kind: FinanceIncomeSourceOption['kind']): string { return ({ Salary: 'Sueldo', Extra: 'Ingreso extra', AnnualBonus: 'Bono anual', IndependentWork: 'Trabajo independiente', Other: 'Otro' })[kind]; }
  hasStaleNotice(): boolean { return /cambi[oó]|actualizá/i.test(this.notice); }

  override ngOnChanges(changes: SimpleChanges): void {
    super.ngOnChanges(changes);
    if (changes['workspaceId'] || changes['contextGeneration']) {
      this.generation++;
      this.queryGeneration++;
      this.catalog = { categories: [], merchants: [], incomeSources: [] };
      this.resetDrafts();
      if (this.workspaceId && this.permissions?.read) void this.load();
    }
  }
  override hasPendingDraft(): boolean { return !!this.catalogDraft.name.trim() || !!this.sourceDraft.name.trim() || this.editOpen; }
  protected override resetForContextChange(): void { this.generation++; this.queryGeneration++; this.catalog = { categories: [], merchants: [], incomeSources: [] }; this.resetDrafts(); }
  private resetDrafts(): void { this.catalogDraft.name = ''; this.catalogDraft.kind = 'Expense'; this.catalogDraft.resource = 'categories'; this.sourceDraft.name = ''; this.sourceDraft.kind = 'Salary'; this.editItem = null; this.editName = ''; this.editOpen = false; }
  async load(): Promise<void> {
    const ws = this.workspaceId; if (!ws || !this.permissions?.read) { this.catalog = { categories: [], merchants: [], incomeSources: [] }; return; }
    const epoch = this.captureContext(); const generation = this.generation; const query = ++this.queryGeneration; this.loading = true;
    try { const data = await this.api.financeCatalog(); if (this.isCurrentContext(epoch) && generation === this.generation && query === this.queryGeneration && ws === this.workspaceId) this.catalog = data; }
    catch (error) { if (this.isCurrentContext(epoch) && generation === this.generation && query === this.queryGeneration) this.showLoadError(error); }
    finally { if (this.isCurrentContext(epoch) && generation === this.generation && query === this.queryGeneration) { this.loading = false; this.changeDetector.markForCheck(); } }
  }
  private resource(item?: CatalogItem): FinanceCatalogResource { if (this.view === 'sources') return 'income-sources'; if (item && 'merchantId' in item) return 'merchants'; return 'categories'; }
  itemId(item: CatalogItem): string { return 'itemId' in item ? item.itemId : 'merchantId' in item ? item.merchantId : item.incomeSourceId; }
  itemKind(item: CatalogItem): string { return 'kind' in item ? item.kind : ''; }
  async createItem(): Promise<void> {
    if (!this.canManage || !this.newName.trim()) return;
    const resource = this.view === 'sources' ? 'income-sources' : this.newResource; const payload = { name: this.newName.trim(), ...(resource === 'categories' ? { kind: this.newKind as OperationKind } : resource === 'income-sources' ? { kind: this.newKind as FinanceIncomeSourceOption['kind'] } : {}) };
    const request = { id: this.requestId(`catalog-create:${resource}`, payload), ...payload };
    if (!(await this.runMutation(() => resource === 'income-sources' ? this.api.createIncomeSource(request) : this.api.createCatalogItem(resource, request), 'Elemento creado.', () => this.load()))) return;
    this.releaseRequestId(`catalog-create:${resource}`, payload); this.newName = '';
  }
  openEdit(item: CatalogItem): void { if (!this.canManage || item.isArchived || ('isDefault' in item && item.isDefault)) return; this.editItem = item; this.editName = item.name; this.editOpen = true; }
  closeEdit(): void { if (!this.saving) this.editOpen = false; }
  async saveEdit(): Promise<void> {
    const item = this.editItem; if (!item || !this.canManage || !this.editName.trim()) return;
    const resource = this.resource(item); const payload = { version: item.version, name: this.editName.trim() };
    const request = { requestId: this.requestId(`catalog-update:${resource}:${this.itemId(item)}`, payload), ...payload };
    if (!(await this.runMutation(() => this.api.updateCatalogItem(resource, this.itemId(item), request), 'Cambios guardados.', () => this.load()))) { if (this.isStale(this.error)) this.notice = 'El elemento cambió en otro lugar. Revisá los datos y volvé a cargar antes de guardar.'; return; }
    this.releaseRequestId(`catalog-update:${resource}:${this.itemId(item)}`, payload); this.closeEdit();
  }
  async archiveItem(item: CatalogItem): Promise<void> {
    if (!this.canManage || item.isArchived || ('isDefault' in item && item.isDefault)) return;
    const resource = this.resource(item); const payload = { version: item.version };
    const request = { requestId: this.requestId(`catalog-archive:${resource}:${this.itemId(item)}`, payload), ...payload };
    if (await this.runMutation(() => this.api.archiveCatalogItem(resource, this.itemId(item), request), 'Elemento archivado.', () => this.load())) this.releaseRequestId(`catalog-archive:${resource}:${this.itemId(item)}`, payload);
    else if (this.isStale(this.error)) this.notice = 'El elemento cambió en otro lugar. Actualizá la lista antes de volver a intentar.';
  }
  async reloadStale(): Promise<void> {
    const selected = this.editItem; await this.load();
    if (!selected) return;
    const updated = [...this.catalog.categories, ...this.catalog.merchants, ...this.catalog.incomeSources].find(item => this.itemId(item) === this.itemId(selected));
    if (updated) this.editItem = updated;
    this.error = '';
    this.notice = 'Lista actualizada. Revisá el nombre y guardá nuevamente.';
  }
  private isStale(message: string): boolean { return this.lastFailure instanceof ApiProblem && this.lastFailure.status === 409 || /versi[oó]n|actualiz|conflicto/i.test(message); }
}
