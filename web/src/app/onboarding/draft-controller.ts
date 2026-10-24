import { AccountInput, CardInput, OnboardingState } from '../core/api.models';
import { ApiProblem } from '../core/api.service';
import { createAccountDraft, createCardDraft } from './draft';
export interface OnboardingGateway {
  loadOnboarding(): Promise<OnboardingState>;
  saveDraft(state: Pick<OnboardingState, 'version' | 'accounts' | 'cards'>): Promise<OnboardingState>;
  completeDraft(request: { version: number }): Promise<OnboardingState>;
}
export class OnboardingDraftController {
  version = 0; completed = false; accounts: AccountInput[] = []; cards: CardInput[] = []; error = ''; saving = false;
  constructor(private readonly api: OnboardingGateway) {}
  async restore(): Promise<void> { this.error = ''; try { this.apply(await this.api.loadOnboarding()); } catch (error) { this.error = message(error); } }
  addAccount(): void { this.accounts = [...this.accounts, createAccountDraft()]; }
  addCard(): void { this.cards = [...this.cards, createCardDraft()]; }
  async save(): Promise<boolean> {
    this.error = ''; this.saving = true;
    try { this.apply(await this.api.saveDraft({ version: this.version, accounts: this.accounts, cards: this.cards })); return true; }
    catch (error) {
      this.error = error instanceof ApiProblem && error.status === 409
        ? `Hay una versión más nueva del borrador. Tus cambios siguen acá; recargá para comparar antes de volver a guardar. ${error.message}`
        : message(error);
      return false;
    } finally { this.saving = false; }
  }
  async confirm(): Promise<boolean> {
    if (!(await this.save())) return false;
    try { this.apply(await this.api.completeDraft({ version: this.version })); return this.completed; }
    catch (error) { this.error = message(error); return false; }
  }
  removeAccount(id: string): void { this.accounts = this.accounts.filter(account => account.id !== id); }
  removeCard(id: string): void { this.cards = this.cards.filter(card => card.id !== id); }
  private apply(state: OnboardingState): void {
    this.version = state.version; this.completed = state.completed;
    this.accounts = state.accounts.map(account => ({ ...account })); this.cards = state.cards.map(card => ({ ...card }));
  }
}
function message(error: unknown): string { return error instanceof Error ? error.message : 'Ocurrió un error. Intentá de nuevo.'; }
