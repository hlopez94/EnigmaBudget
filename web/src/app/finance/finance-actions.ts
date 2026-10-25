import { ChangeDetectorRef } from '@angular/core';
import { ApiService } from '../core/api.service';
import { Currency } from '../core/api.models';
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

export abstract class FinanceActions {
  saving = false;
  error = '';
  notice = '';
  private readonly idempotencyKeys = new Map<string, string>();

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
    this.saving = true;
    this.error = '';
    this.notice = '';
    this.changeDetector.markForCheck();
    try {
      await mutation();
      this.notice = success;
      if (afterSuccess) {
        try {
          await afterSuccess();
        } catch (error) {
          this.showLoadError(error);
        }
      }
      return true;
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'No pudimos guardar los cambios.';
      return false;
    } finally {
      this.saving = false;
      this.changeDetector.markForCheck();
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
