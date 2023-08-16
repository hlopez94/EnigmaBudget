import { Routes } from '@angular/router';
import { canActivateAuth } from '../auth/auth-guard';

export const REPORTES_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./reportes.component').then((c) => c.ReportesComponent),
    canActivate: [canActivateAuth],
  },
];
