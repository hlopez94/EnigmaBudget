import "@angular/compiler";
import { describe, expect, it, vi } from 'vitest';
import { App } from './app';
import { ApiService } from './core/api.service';
import { OnboardingState, SessionInfo } from './core/api.models';

describe('application startup', () => {
  it('refreshes the root view after an async session opens the incomplete wizard', async () => {
    const session: SessionInfo = { authenticated:true, displayName:'Usuario', googleConfigured:false, developmentLoginEnabled:false, csrfToken:'token' };
    const state: OnboardingState = { version:2, completed:false, accounts:[], cards:[] };
    const api = { session:vi.fn().mockResolvedValue(session), loadOnboarding:vi.fn().mockResolvedValue(state) };
    const markForCheck = vi.fn();
    const app = new App(api as unknown as ApiService, { markForCheck } as unknown as import('@angular/core').ChangeDetectorRef);

    await app.load();

    expect(app.busy).toBe(false);
    expect(app.session).toEqual(session);
    expect(app.state).toEqual(state);
    expect(markForCheck).toHaveBeenCalled();
  });
});
