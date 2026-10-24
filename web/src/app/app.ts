import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { AccountSummary, CardSummary, OnboardingState, SessionInfo } from './core/api.models';
import { ApiService } from './core/api.service';
import { DashboardComponent } from './dashboard/dashboard';
import { OnboardingComponent } from './onboarding/onboarding';
import { SignInComponent } from './onboarding/sign-in';
import { ButtonModule } from '@openng/optimus-ui/button';
import { MessageModule } from '@openng/optimus-ui/message';
@Component({ selector: 'app-root', standalone: true, imports: [ButtonModule, MessageModule, DashboardComponent, OnboardingComponent, SignInComponent], templateUrl: './app.html', styleUrl: './app.css' })
export class App implements OnInit {
  constructor(private readonly api: ApiService, private readonly changeDetector: ChangeDetectorRef) {} session: SessionInfo | null = null; state: OnboardingState | null = null;
  accounts: AccountSummary[] = []; cards: CardSummary[] = []; busy = true; error = '';
  async ngOnInit(): Promise<void> { await this.load(); }
  async load(): Promise<void> {
    this.busy = true; this.error = '';
    try { this.session = await this.api.session(); if (this.session.authenticated) { this.state = await this.api.loadOnboarding(); if (this.state.completed) [this.accounts, this.cards] = await Promise.all([this.api.accounts(), this.api.cards()]); } }
    catch (error) { this.error = error instanceof Error ? error.message : 'No pudimos cargar tu espacio.'; }
    finally { this.busy = false; this.changeDetector.markForCheck(); }
  }
  async signedIn(): Promise<void> { await this.load(); }
  async completed(state: OnboardingState): Promise<void> { this.state = state; this.changeDetector.markForCheck(); try { [this.accounts, this.cards] = await Promise.all([this.api.accounts(), this.api.cards()]); } catch (error) { this.error = error instanceof Error ? error.message : 'La configuración se guardó, pero no pudimos cargar el resumen.'; } finally { this.changeDetector.markForCheck(); } }
  async logout(): Promise<void> { this.busy = true; try { await this.api.logout(); this.state = null; this.accounts = []; this.cards = []; await this.load(); } catch (error) { this.error = error instanceof Error ? error.message : 'No pudimos cerrar la sesión.'; this.busy = false; this.changeDetector.markForCheck(); } }
}
