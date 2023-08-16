import { Routes } from '@angular/router';
import { canActivateAuth } from '../auth/auth-guard';

export const HOME_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./home.component').then((c) => c.HomeComponent),
    canActivate: [canActivateAuth],
  },
];
