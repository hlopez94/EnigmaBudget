import {  Routes } from '@angular/router';

export const TARJETAS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: ()=> import('./tarjetas.component').then(c=>c.TarjetasComponent)
  }
];

