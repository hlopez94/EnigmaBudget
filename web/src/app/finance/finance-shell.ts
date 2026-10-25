import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from '@openng/optimus-ui/button';
import { FinanceOverview } from '../core/api.models';
import { ApiService } from '../core/api.service';
import { formatMoney } from '../onboarding/draft';
import { FinanceAccounts } from './accounts';
import { FinanceCards } from './cards';
import { FinanceOperations } from './operations';
import { FinanceSharing } from './sharing';

export type FinanceSection = 'home' | 'operations' | 'accounts' | 'cards' | 'sharing';

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
  ],
  templateUrl: './finance-shell.html',
  styleUrl: './finance-shell.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FinanceShell implements OnInit {
  @Input() name = 'tu espacio';
  @Input() setupIncomplete = false;
  @Output() setupRequested = new EventEmitter<void>();

  period = today().slice(0, 7);
  section: FinanceSection = 'home';
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

  constructor(
    private readonly api: ApiService,
    private readonly changeDetector: ChangeDetectorRef,
  ) {}

  ngOnInit(): Promise<void> {
    return this.refresh();
  }

  async refresh(): Promise<void> {
    this.busy = true;
    this.error = '';
    this.notice = '';
    try {
      this.overview = await this.api.financeOverview(this.period);
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'No pudimos cargar los datos.';
    } finally {
      this.busy = false;
      this.changeDetector.markForCheck();
    }
  }

  async changePeriod(): Promise<void> {
    await this.refresh();
  }

  navigate(section: FinanceSection): void {
    this.section = section;
    this.error = '';
    this.notice = '';
    this.changeDetector.markForCheck();
  }

  money(amount: string, currency: 'ARS' | 'USD'): string {
    return formatMoney(amount, currency);
  }
}
