import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { canActivateAuth } from './auth/auth-guard';

export const APP_ROUTES: Routes = [
  {
    path:'',
    loadComponent:() => import('./shell/welcome-page/welcome-page.component').then(c=>c.WelcomePageComponent)
  },
  {
    path:'user-dashboard',
    loadChildren: () => import('./home/home-routing.module').then(m => m.HOME_ROUTES),
    canActivate:[canActivateAuth]
  },
  {
    path:'tarjetas',
    loadChildren: () => import('./tarjetas/tarjetas-routing.module').then(m => m.TARJETAS_ROUTES),
    canActivate:[canActivateAuth]
  },
  {
    path:'cuentas',
    loadChildren: () => import('./cuentas/cuentas-routing.module').then(m => m.CUENTAS_ROUTES),
    canActivate:[canActivateAuth]
  },
  {
    path:'reportes',
    loadChildren: () => import('./reportes/reportes-routing.module').then(m => m.REPORTES_ROUTES),
    canActivate:[canActivateAuth]
  },
  {
    path:'tarjetas',
    loadChildren: () => import('./tarjetas/tarjetas-routing.module').then(m => m.TARJETAS_ROUTES),
    canActivate:[canActivateAuth]
  },
  {
    path:'auth',
    loadChildren: () => import('./auth/auth-routing.module').then(m => m.AUTH_ROUTES)
  },
  {
    path: '*',
    redirectTo: ''
  }
];
