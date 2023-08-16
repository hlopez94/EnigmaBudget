import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { Route } from '@angular/router';
import { AUTH_ROUTES } from '../auth-routing.module';
import { canActivateAuth } from '../auth-guard';

describe('Auth Routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RouterTestingModule],
    });
  });

  it('should have the correct number of routes', () => {
    expect(AUTH_ROUTES.length).toBe(5); // Adjust the expected number of routes
  });

  it('should have the correct route configuration for /profile', () => {
    console.log(AUTH_ROUTES);
    const profileRoute: Route  = AUTH_ROUTES.find(route => route.path === 'profile')!;

    expect(profileRoute).toBeTruthy();
    expect(profileRoute!.canActivate).toContain(canActivateAuth);
  });

  // Add more tests for other routes as needed
});