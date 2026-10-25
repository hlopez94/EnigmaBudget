import { ChangeDetectorRef, Directive, Input, OnChanges, OnDestroy, SimpleChanges } from '@angular/core';
import { ApiService } from '../core/api.service';
import { Currency, WorkspacePermissions } from '../core/api.models';
import { formatMoney, normalizeDecimal } from '../onboarding/draft';

export type RefreshFinanceOverview = () => Promise<void>;
export const financeId = (): string => crypto.randomUUID();
export const financeToday = (): string => {
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
export const financePeriod = (): string => financeToday().slice(0, 7);

@Directive()
export abstract class FinanceActions implements OnChanges, OnDestroy {
  private permissionState: WorkspacePermissions | null = null;
  private archivedState = false;
  @Input() set permissions(value: WorkspacePermissions | null) {
    this.permissionState = value;
    this.permissionsChanged();
  }
  get permissions(): WorkspacePermissions | null { return this.permissionState; }
  @Input() set workspaceArchived(value: boolean) {
    this.archivedState = value;
    if (value) this.permissionsChanged();
  }
  get workspaceArchived(): boolean { return this.archivedState; }
  @Input() workspaceId: string | null = null;
  @Input() contextGeneration = 0;
  saving = false;
  error = '';
  notice = '';
  private readonly idempotencyKeys = new Map<string, string>();
  private contextEpoch = 0;
  private destroyed = false;

  ngOnChanges(changes: SimpleChanges = {}): void {
    if (changes['workspaceId'] || changes['contextGeneration']) this.invalidateContext();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.contextEpoch++;
    this.idempotencyKeys.clear();
  }

  invalidateContext(): void {
    this.contextEpoch++;
    this.idempotencyKeys.clear();
    this.saving = false;
    this.error = '';
    this.notice = '';
    this.resetForContextChange();
    this.changeDetector.markForCheck();
  }

  hasPendingDraft(): boolean {
    return false;
  }
  protected allowed(permission: keyof WorkspacePermissions): boolean {
    return !this.workspaceArchived && (this.permissions?.[permission] ?? true);
  }
  protected permissionsChanged(): void {}
  protected resetForContextChange(): void {}

  protected captureContext(): number { return this.contextEpoch; }
  protected isCurrentContext(epoch: number): boolean { return !this.destroyed && epoch === this.contextEpoch; }

  protected constructor(
    protected readonly api: ApiService,
    protected readonly changeDetector: ChangeDetectorRef,
  ) {}

  money(amount: string, currency: Currency): string {
    return formatMoney(amount, currency);
  }

  protected decimal(value: string): string {
    return normalizeDecimal(value);
  }

  protected requestId(scope: string, payload: unknown): string {
    const key = `${scope}:${JSON.stringify(payload)}`;
    let id = this.idempotencyKeys.get(key);
    if (!id) {
      id = financeId();
      this.idempotencyKeys.set(key, id);
    }
    return id;
  }

  protected releaseRequestId(scope: string, payload: unknown): void {
    this.idempotencyKeys.delete(`${scope}:${JSON.stringify(payload)}`);
  }

  protected day(value: string): number | null {
    return value.trim() ? Number(value) : null;
  }

  protected async runMutation(
    mutation: () => Promise<unknown>,
    success: string,
    afterSuccess?: () => Promise<void>,
  ): Promise<boolean> {
    if (this.saving) return false;
    const contextEpoch = this.captureContext();
    this.saving = true;
    this.error = '';
    this.notice = '';
    this.changeDetector.markForCheck();
    try {
      await mutation();
      if (!this.isCurrentContext(contextEpoch)) return false;
      this.notice = success;
      if (afterSuccess) {
        try {
          await afterSuccess();
        } catch (error) {
          if (this.isCurrentContext(contextEpoch)) this.showLoadError(error);
        }
      }
      return this.isCurrentContext(contextEpoch);
    } catch (error) {
      if (!this.isCurrentContext(contextEpoch)) return false;
      this.error = error instanceof Error ? error.message : 'No pudimos guardar los cambios.';
      return false;
    } finally {
      if (this.isCurrentContext(contextEpoch)) {
        this.saving = false;
        this.changeDetector.markForCheck();
      }
    }
  }

  protected message(error: unknown): string {
    return error instanceof Error ? error.message : 'No pudimos cargar los datos.';
  }

  protected showLoadError(error: unknown): void {
    this.error = this.message(error);
    this.changeDetector.markForCheck();
  }
}
