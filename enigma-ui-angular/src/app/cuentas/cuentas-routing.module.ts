import { Routes } from '@angular/router';

export const CUENTAS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./cuentas.component').then((c) => c.CuentasComponent),
  },
];
