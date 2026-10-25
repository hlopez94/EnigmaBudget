import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  QueryList,
  SimpleChanges,
  ViewChildren,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from '@openng/optimus-ui/button';
import { FinanceOverview, Workspace } from '../core/api.models';
import { ApiProblem, ApiService } from '../core/api.service';
import { formatMoney } from '../onboarding/draft';
import { FinanceAccounts } from './accounts';
import { FinanceCards } from './cards';
import { FinanceOperations } from './operations';
import { FinanceSharing } from './sharing';
import { WorkspaceManager } from '../workspaces/workspace-manager';
import { ActionDialog } from './ui/action-dialog';
import { FinanceSavings } from '../savings/savings';
import { FinanceProjection } from '../projection/projection';
import { FinanceCatalogScreen } from './catalog';
import { FinanceBudgets } from './budgets';

export type FinanceSection = 'home' | 'operations' | 'accounts' | 'cards' | 'sharing' | 'more' | 'spaces' | 'savings' | 'projection' | 'exchange' | 'catalog' | 'sources' | 'budgets';
type MoreSection = 'accounts' | 'savings' | 'projection' | 'exchange' | 'sharing' | 'spaces' | 'catalog' | 'sources' | 'budgets';

const today = (): string => {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Argentina/Buenos_Aires',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const part = (type: 'year' | 'month' | 'day'): string =>
    parts.find((value) => value.type === type)?.value ?? '';
  return [part('year'), part('month'), part('day')].join('-');
};

const monthName = new Intl.DateTimeFormat('es-AR', {
  month: 'long',
  timeZone: 'UTC',
});

const isoMonthParts = (value: string): { year: number; month: number } | null => {
  const match = /^(\d{4})-(0[1-9]|1[0-2])$/.exec(value);
  if (!match || match[0] !== value) return null;
  const year = Number(match[1]);
  return year > 0 ? { year, month: Number(match[2]) } : null;
};
const periodParts = (value: string): { year: number; month: number } | null => {
  const parts = isoMonthParts(value);
  return parts && parts.year >= 2000 && parts.year <= 9999 ? parts : null;
};

@Component({
  selector: 'app-finance-shell',
  standalone: true,
  imports: [
    FormsModule,
    ButtonModule,
    FinanceAccounts,
    FinanceOperations,
    FinanceCards,
    FinanceSharing,
    WorkspaceManager,
    ActionDialog,
    FinanceSavings,
    FinanceProjection,
    FinanceCatalogScreen,
    FinanceBudgets,
  ],
  templateUrl: './finance-shell.html',
  styleUrl: './finance-shell.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FinanceShell implements OnInit, OnChanges {
  @Input() name = 'tu espacio';
  @Input() setupIncomplete = false;
  @Input() workspaces: Workspace[] = [];
  @Input() activeWorkspace: Workspace | null = null;
  @Input() personalWorkspaceId = '';
  @Input() currentUserId = '';
  @Input() googleConfigured = false;
  @Input() pendingInviteToken: string | null = null;
  @Output() setupRequested = new EventEmitter<void>();
  @Output() workspaceSelected = new EventEmitter<string>();
  @Output() workspacesChanged = new EventEmitter<void>();
  @Output() workspaceUpdated = new EventEmitter<Workspace>();

  @ViewChildren(FinanceAccounts) accountComponents!: QueryList<FinanceAccounts>;
  @ViewChildren(FinanceOperations) operationComponents!: QueryList<FinanceOperations>;
  @ViewChildren(FinanceCards) cardComponents!: QueryList<FinanceCards>;
  @ViewChildren(FinanceSharing) sharingComponents!: QueryList<FinanceSharing>;
  @ViewChildren(WorkspaceManager) workspaceManagers!: QueryList<WorkspaceManager>;
  @ViewChildren(FinanceSavings) savingsComponents!: QueryList<FinanceSavings>;
  @ViewChildren(FinanceProjection) projectionComponents!: QueryList<FinanceProjection>;
  @ViewChildren(FinanceCatalogScreen) catalogComponents!: QueryList<FinanceCatalogScreen>;
  @ViewChildren(FinanceBudgets) budgetComponents!: QueryList<FinanceBudgets>;
  private workspaceManagerInstance: WorkspaceManager | null = null;

  period = today().slice(0, 7);
  appliedPeriod = this.period;
  private periodRequestGeneration = 0;
  get periodLabel(): string {
    const period = this.overview?.period ?? this.appliedPeriod;
    const parts = isoMonthParts(period);
    if (!parts) return '';
    const date = new Date(0);
    date.setUTCFullYear(parts.year, parts.month - 1, 1);
    return `${monthName.format(date)} ${period.slice(0, 4)}`;
  }
  section: FinanceSection = 'home';
  requestedWorkspaceId = '';
  workspacePickerValue = '';
  workspaceGeneration = 0;
  private initializedWorkspaceId: string | null = null;
  private hasInitializedWorkspace = false;
  overview: FinanceOverview | null = null;
  busy = false;
  error = '';
  notice = '';
  readonly refreshOverview = (): Promise<void> => this.refresh();

  get activeAccounts() {
    return (this.overview?.accounts ?? []).filter((account) => !account.isArchived);
  }
  get cards() {
    return this.overview?.cards ?? [];
  }
  workspaceLabel(workspace: Workspace): string {
    if (workspace.kind === 'Personal') {
      if (workspace.id === this.personalWorkspaceId) return 'Mi espacio Personal';
      return `Personal de ${workspace.ownerDisplayName?.trim() || 'otra persona'} · Solo lectura`;
    }
    return `${workspace.name} · ${workspace.role === 'Reader' ? 'Solo lectura' : workspace.role === 'Owner' ? 'Titular' : workspace.role === 'Admin' ? 'Administración' : 'Integrante'}`;
  }

  constructor(
    private readonly api: ApiService,
    private readonly changeDetector: ChangeDetectorRef,
  ) {}

  ngOnInit(): Promise<void> {
    this.initializedWorkspaceId = this.activeWorkspace?.id ?? null;
    this.workspacePickerValue = this.activeWorkspace?.id ?? '';
    this.hasInitializedWorkspace = true;
    this.api.selectWorkspace(this.initializedWorkspaceId);
    return this.refresh();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['pendingInviteToken'] && this.pendingInviteToken) this.section = 'spaces';
    if (changes['activeWorkspace'] && this.hasInitializedWorkspace && this.activeWorkspace?.id !== this.initializedWorkspaceId) {
      this.invalidateFinanceChildren();
      this.initializedWorkspaceId = this.activeWorkspace?.id ?? null;
      this.workspacePickerValue = this.activeWorkspace?.id ?? '';
      this.api.selectWorkspace(this.initializedWorkspaceId);
      this.workspaceGeneration++;
      this.overview = null;
      this.section = 'home';
      this.error = '';
      this.notice = '';
      void this.refresh();
    }
  }

  async refresh(): Promise<void> {
    const requestedPeriod = this.period;
    if (!periodParts(requestedPeriod)) {
      this.error = 'Elegí un período válido para consultar tus finanzas.';
      this.changeDetector.markForCheck();
      return;
    }
    const workspaceId = this.activeWorkspace?.id ?? null;
    const generation = this.workspaceGeneration;
    const periodRequestGeneration = ++this.periodRequestGeneration;
    this.busy = true;
    this.error = '';
    this.notice = '';
    if (!workspaceId) {
      this.overview = null;
      this.busy = false;
      this.error = 'Elegí un espacio disponible para ver sus finanzas.';
      this.changeDetector.markForCheck();
      return;
    }
    try {
      const workspace = await this.api.workspace(workspaceId);
      if (generation !== this.workspaceGeneration || workspaceId !== this.activeWorkspace?.id) return;
      const overview = await this.api.financeOverview(requestedPeriod);
      if (generation === this.workspaceGeneration && periodRequestGeneration === this.periodRequestGeneration && workspaceId === this.activeWorkspace?.id) {
        if (JSON.stringify(workspace) !== JSON.stringify(this.activeWorkspace)) {
          this.activeWorkspace = workspace;
          this.workspaceUpdated.emit(workspace);
        }
        this.overview = overview;
        this.appliedPeriod = requestedPeriod;
      }
    } catch (error) {
      if (generation === this.workspaceGeneration && periodRequestGeneration === this.periodRequestGeneration && workspaceId === this.activeWorkspace?.id) {
        if (error instanceof ApiProblem && (error.status === 403 || error.status === 404)) this.overview = null;
        this.error = error instanceof Error ? error.message : 'No pudimos cargar los datos.';
        if (error instanceof ApiProblem && (error.status === 403 || error.status === 404)) this.workspacesChanged.emit();
      }
    } finally {
      if (generation === this.workspaceGeneration && periodRequestGeneration === this.periodRequestGeneration) {
        this.busy = false;
        this.changeDetector.markForCheck();
      }
    }
  }

  get permissions() { return this.activeWorkspace?.permissions; }
  get moreLinks(): { section: MoreSection; label: string; description: string }[] {
    const links: { section: MoreSection; label: string; description: string }[] = [
      { section: 'accounts', label: 'Cuentas', description: 'Ver saldos, disponibilidad y administrar cuentas' },
      { section: 'savings', label: 'Ahorros', description: 'Objetivos, reservas y aportes' },
      { section: 'projection', label: 'Proyección', description: 'Saldos futuros, compromisos y escenarios' },
      { section: 'exchange', label: 'Cotizaciones', description: 'Referencias guardadas de compra y venta' },
      { section: 'catalog', label: 'Categorías y comercios', description: 'Organizar categorías y comercios' },
      { section: 'sources', label: 'Fuentes de ingresos', description: 'Administrar las fuentes de ingreso' },
      { section: 'budgets', label: 'Presupuesto', description: 'Límites y consumo mensual por categoría' },
    ];
    if (this.canReadGroups) links.push({ section: 'sharing', label: 'Compartidos', description: 'Grupos y gastos compartidos' });
    links.push({ section: 'spaces', label: 'Espacios y privacidad', description: 'Integrantes, permisos y configuración del espacio' });
    return links;
  }
  get canReadGroups(): boolean { return !!this.permissions?.readGroups; }
  get canWrite(): boolean { return !!this.permissions?.recordFinance && !this.activeWorkspace?.isArchived; }
  get canManageResources(): boolean { return !!this.permissions?.manageResources && !this.activeWorkspace?.isArchived; }
  get saving(): boolean { return [...(this.accountComponents?.toArray() ?? []), ...(this.operationComponents?.toArray() ?? []), ...(this.cardComponents?.toArray() ?? []), ...(this.sharingComponents?.toArray() ?? []), ...(this.savingsComponents?.toArray() ?? []), ...(this.projectionComponents?.toArray() ?? []), ...(this.catalogComponents?.toArray() ?? []), ...(this.budgetComponents?.toArray() ?? [])].some(component => component.saving) || (this.workspaceManagers?.toArray() ?? []).some(component => component.busy); }
  get hasPendingDraft(): boolean { return [...(this.accountComponents?.toArray() ?? []), ...(this.operationComponents?.toArray() ?? []), ...(this.cardComponents?.toArray() ?? []), ...(this.sharingComponents?.toArray() ?? []), ...(this.savingsComponents?.toArray() ?? []), ...(this.projectionComponents?.toArray() ?? []), ...(this.catalogComponents?.toArray() ?? []), ...(this.budgetComponents?.toArray() ?? [])].some(component => component.hasPendingDraft()) || (this.workspaceManagers?.toArray() ?? []).some(component => component.hasPendingDraft()); }

  private invalidateFinanceChildren(): void {
    this.workspaceGeneration++;
    [...(this.accountComponents?.toArray() ?? []), ...(this.operationComponents?.toArray() ?? []), ...(this.cardComponents?.toArray() ?? []), ...(this.sharingComponents?.toArray() ?? []), ...(this.savingsComponents?.toArray() ?? []), ...(this.projectionComponents?.toArray() ?? []), ...(this.catalogComponents?.toArray() ?? []), ...(this.budgetComponents?.toArray() ?? [])].forEach(component => component.invalidateContext());
    this.workspaceManagerInstance?.invalidateContext();
  }

  registerWorkspaceManager(manager: WorkspaceManager): void { this.workspaceManagerInstance = manager; }

  private emitWorkspaceSelection(id: string): void {
    this.invalidateFinanceChildren();
    this.workspaceSelected.emit(id);
  }

  requestWorkspaceChange(id: string): void {
    if (!id || id === this.activeWorkspace?.id) return;
    this.workspacePickerValue = id;
    if (this.saving) {
      this.workspacePickerValue = this.activeWorkspace?.id ?? '';
      this.notice = 'Esperá a que termine el guardado antes de cambiar de espacio.';
      this.changeDetector.markForCheck();
      return;
    }
    if (this.hasPendingDraft) {
      this.requestedWorkspaceId = id;
      this.changeDetector.markForCheck();
      return;
    }
    this.emitWorkspaceSelection(id);
  }
  confirmWorkspaceChange(): void {
    if (this.saving || !this.requestedWorkspaceId) return;
    const id = this.requestedWorkspaceId;
    this.requestedWorkspaceId = '';
    this.invalidateFinanceChildren();
    if (id.startsWith('@period:')) {
      this.period = id.slice('@period:'.length);
      void this.refresh();
      return;
    }
    if (id.startsWith('@section:')) {
      this.section = id.slice('@section:'.length) as FinanceSection;
      this.changeDetector.markForCheck();
      return;
    }
    if (id === 'setup') this.setupRequested.emit();
    else this.workspaceSelected.emit(id);
  }
  cancelWorkspaceChange(): void { this.requestedWorkspaceId = ''; this.workspacePickerValue = this.activeWorkspace?.id ?? ''; this.period = this.appliedPeriod; this.changeDetector.markForCheck(); }

  async changePeriod(): Promise<void> {
    const requested = this.period;
    if (this.saving) { this.period = this.appliedPeriod; this.notice = 'Esperá a que termine el guardado antes de cambiar el período.'; this.changeDetector.markForCheck(); return; }
    if (!periodParts(requested)) {
      this.error = 'Elegí un período válido para consultar tus finanzas.';
      this.changeDetector.markForCheck();
      return;
    }
    if (requested === this.appliedPeriod) { await this.refresh(); return; }
    if (this.hasPendingDraft) { this.period = this.appliedPeriod; this.requestedWorkspaceId = `@period:${requested}`; this.changeDetector.markForCheck(); return; }
    await this.refresh();
  }

  navigate(section: FinanceSection): void {
    if (section === 'sharing' && !this.canReadGroups) return;
    if (section === this.section) return;
    if (this.saving) { this.notice = 'Esperá a que termine el guardado antes de cambiar de sección.'; this.changeDetector.markForCheck(); return; }
    this.section = section;
    this.error = '';
    this.notice = '';
    this.changeDetector.markForCheck();
  }

  navigateMore(target: MoreSection | 'setup'): void {
    if (target === 'accounts') this.navigate('accounts');
    else if (target !== 'setup') this.navigate(target);
    else if (this.saving) {
      this.notice = 'Esperá a que termine el guardado antes de abrir la configuración.';
      this.changeDetector.markForCheck();
    } else if (this.hasPendingDraft) {
      this.requestedWorkspaceId = 'setup';
      this.changeDetector.markForCheck();
    } else this.setupRequested.emit();
  }

  managerBusyChanged(): void { this.changeDetector.markForCheck(); }

  money(amount: string, currency: 'ARS' | 'USD'): string {
    return formatMoney(amount, currency);
  }
}
