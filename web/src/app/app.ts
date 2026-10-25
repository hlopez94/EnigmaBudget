import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { OnboardingState, SessionInfo } from './core/api.models';
import { ApiService } from './core/api.service';
import { OnboardingComponent } from './onboarding/onboarding';
import { SignInComponent } from './onboarding/sign-in';
import { ButtonModule } from '@openng/optimus-ui/button';
import { MessageModule } from '@openng/optimus-ui/message';
import { FinanceShell } from './finance/finance-shell';
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [ButtonModule, MessageModule, OnboardingComponent, SignInComponent, FinanceShell],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  constructor(
    private readonly api: ApiService,
    private readonly changeDetector: ChangeDetectorRef,
  ) {}
  session: SessionInfo | null = null;
  state: OnboardingState | null = null;
  financeAvailable = false;
  showSetup = false;
  busy = true;
  error = '';
  async ngOnInit(): Promise<void> {
    await this.load();
  }
  async load(): Promise<void> {
    this.busy = true;
    this.error = '';
    try {
      this.session = await this.api.session();
      if (this.session.authenticated) {
        this.state = await this.api.loadOnboarding();
        this.financeAvailable = true;
      }
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'No pudimos cargar tu espacio.';
    } finally {
      this.busy = false;
      this.changeDetector.markForCheck();
    }
  }
  async signedIn(): Promise<void> {
    await this.load();
  }
  async completed(state: OnboardingState): Promise<void> {
    this.state = state;
    this.showSetup = false;
    this.financeAvailable = true;
    this.changeDetector.markForCheck();
  }
  startSetup(): void {
    this.showSetup = true;
    this.changeDetector.markForCheck();
  }
  async logout(): Promise<void> {
    this.busy = true;
    try {
      await this.api.logout();
      this.state = null;
      this.financeAvailable = false;
      this.showSetup = false;
      await this.load();
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'No pudimos cerrar la sesión.';
      this.busy = false;
      this.changeDetector.markForCheck();
    }
  }
}
