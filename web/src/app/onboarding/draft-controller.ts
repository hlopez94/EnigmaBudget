import { AccountInput, CardInput, OnboardingState } from '../core/api.models';
import { ApiProblem } from '../core/api.service';
import { createAccountDraft, createCardDraft } from './draft';

export interface OnboardingGateway {
  loadOnboarding(): Promise<OnboardingState>;
  saveDraft(state: Pick<OnboardingState, 'version' | 'accounts' | 'cards'>): Promise<OnboardingState>;
  completeDraft(request: { version: number }): Promise<OnboardingState>;
}

export class OnboardingDraftController {
  version = 0;
  completed = false;
  accounts: AccountInput[] = [];
  cards: CardInput[] = [];
  error = '';
  saving = false;
  conflict = false;
  latestState: OnboardingState | null = null;

  constructor(private readonly api: OnboardingGateway) {}

  async restore(): Promise<void> {
    this.error = '';
    try { this.apply(await this.api.loadOnboarding()); }
    catch (error) { this.error = message(error); }
  }

  addAccount(): void { this.accounts = [...this.accounts, createAccountDraft()]; }
  addCard(): void { this.cards = [...this.cards, createCardDraft()]; }

  async save(): Promise<boolean> {
    if (this.saving || this.conflict) return false;
    this.error = '';
    this.saving = true;
    const submitted = {
      version: this.version,
      accounts: this.accounts.map(account => ({ ...account })),
      cards: this.cards.map(card => ({ ...card })),
    };
    const submittedData = JSON.stringify({ accounts: submitted.accounts, cards: submitted.cards });
    try {
      const saved = await this.api.saveDraft(submitted);
      const currentData = JSON.stringify({ accounts: this.accounts, cards: this.cards });
      if (currentData === submittedData) this.apply(saved);
      else { this.version = saved.version; this.completed = saved.completed; }
      return true;
    } catch (error) {
      if (error instanceof ApiProblem && error.status === 409) {
        this.conflict = true;
        this.latestState = null;
        this.error = `El borrador cambió en otro lugar. Tus cambios siguen acá. Cargá la última versión para comparar y elegir cómo continuar.`;
      } else this.error = message(error);
      return false;
    } finally { this.saving = false; }
  }

  async refreshLatest(): Promise<boolean> {
    if (!this.conflict || this.saving) return false;
    this.error = '';
    this.saving = true;
    try {
      this.latestState = await this.api.loadOnboarding();
      if (this.latestState.completed) this.error = 'La configuración ya se confirmó. Podés cargar esa versión; tus cambios locales no se enviarán.';
      return true;
    } catch (error) {
      this.error = `No pudimos cargar la última versión. Tus cambios siguen acá. ${message(error)}`;
      return false;
    } finally { this.saving = false; }
  }

  useLatestVersion(): OnboardingState | null {
    if (!this.latestState) return null;
    const latest = this.latestState;
    this.apply(latest);
    this.latestState = null;
    this.conflict = false;
    this.error = '';
    return latest;
  }

  async saveLocalOverLatest(): Promise<boolean> {
    if (!this.latestState && !(await this.refreshLatest())) return false;
    if (!this.latestState) return false;
    if (this.latestState.completed) {
      this.error = 'La configuración ya se confirmó. Tus cambios locales se conservan, pero no se pueden guardar.';
      return false;
    }
    this.version = this.latestState.version;
    this.latestState = null;
    this.conflict = false;
    this.error = '';
    return this.save();
  }

  async confirm(): Promise<boolean> {
    if (!(await this.save())) return false;
    try { this.apply(await this.api.completeDraft({ version: this.version })); return this.completed; }
    catch (error) { this.error = message(error); return false; }
  }

  removeAccount(id: string): void { this.accounts = this.accounts.filter(account => account.id !== id); }
  removeCard(id: string): void { this.cards = this.cards.filter(card => card.id !== id); }

  private apply(state: OnboardingState): void {
    this.version = state.version;
    this.completed = state.completed;
    this.accounts = state.accounts.map(account => ({ ...account }));
    this.cards = state.cards.map(card => ({ ...card }));
  }
}

function message(error: unknown): string { return error instanceof Error ? error.message : 'Ocurrió un error. Intentá de nuevo.'; }
